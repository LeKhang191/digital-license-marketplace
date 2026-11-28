using System.Security.Claims;
using System.Text.Json;
using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args.Length > 0 && args[0] == "import" ? Array.Empty<string>() : args);
var cs = builder.Configuration.GetConnectionString("LicenseDb")
    ?? throw new InvalidOperationException("LicenseDb required");
var key = Convert.FromBase64String(builder.Configuration["LicenseVault:EncryptionKey"] ?? "");
if (key.Length != 32) throw new InvalidOperationException("LicenseVault requires a 32-byte base64 key");
var maxAttempts = builder.Configuration.GetValue("LicenseVault:MaxAllocationAttempts", 20);
var retryDelaySeconds = builder.Configuration.GetValue("LicenseVault:RetryDelaySeconds", 60);
var source = NpgsqlDataSource.Create(cs);
var store = new VaultStore(source, key);
var inbox = new FulfillmentInbox(source, maxAttempts, retryDelaySeconds);
await store.InitializeAsync();
if (args.Length > 0 && args[0] == "import")
{
    if (args.Length != 2 || !Guid.TryParse(args[1], out var product))
        throw new ArgumentException("Usage: import PRODUCT_UUID; one key per stdin line");
    await store.ImportAsync(product);
    return;
}
await inbox.InitializeAsync();
builder.Services.AddSingleton(store);
builder.Services.AddSingleton(inbox);
builder.Services.AddHostedService<FulfillmentRetryWorker>();
builder.Services.AddHostedService<VaultWorker>();
builder.Services.AddHostedService<DeliveryDispatchWorker>();

var requireHttps = builder.Configuration.GetValue("Keycloak:RequireHttpsMetadata", !builder.Environment.IsDevelopment());
var audience = builder.Configuration["Keycloak:Audience"];
var allowedClient = builder.Configuration["Keycloak:AllowedClientId"] ?? "marketplace-web";

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MetadataAddress = builder.Configuration["Keycloak:MetadataAddress"] ?? throw new InvalidOperationException("Keycloak metadata required");
    o.RequireHttpsMetadata = requireHttps;
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Keycloak:Issuer"] ?? throw new InvalidOperationException("Keycloak issuer required"),
        ValidateAudience = !string.IsNullOrWhiteSpace(audience),
        ValidAudience = audience,
        NameClaimType = "preferred_username",
        RoleClaimType = ClaimTypes.Role
    };
    o.Events = new JwtBearerEvents
    {
        OnTokenValidated = ctx =>
        {
            if (ctx.Principal?.Identity is ClaimsIdentity identity)
            {
                var raw = identity.FindFirst("realm_access")?.Value;
                if (!string.IsNullOrEmpty(raw))
                {
                    using var doc = JsonDocument.Parse(raw);
                    if (doc.RootElement.TryGetProperty("roles", out var roles) && roles.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var role in roles.EnumerateArray())
                        {
                            var value = role.GetString();
                            if (!string.IsNullOrEmpty(value))
                                identity.AddClaim(new Claim(ClaimTypes.Role, value));
                        }
                    }
                }
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(o =>
{
    o.AddPolicy("Library", p => p.RequireAuthenticatedUser().RequireClaim("azp", allowedClient).RequireClaim("sub"));
    o.AddPolicy("AdminOnly", p => p.RequireAuthenticatedUser().RequireClaim("azp", allowedClient).RequireRole("admin"));
});
var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Ok());
app.MapGet("/internal/orders/{id:guid}/keys", async Task<IResult> (Guid id, HttpContext ctx, VaultStore vault, CancellationToken ct) =>
{
    ctx.Response.Headers.CacheControl = "no-store";
    var user = ctx.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(user)) return Results.Unauthorized();
    var keys = await vault.GetKeysAsync(id, user, ct);
    return keys.Count == 0 ? Results.NotFound() : Results.Ok(keys);
}).RequireAuthorization("Library");

app.MapGet("/api/vault/availability", async Task<IResult> (VaultStore vault, CancellationToken ct) =>
    Results.Ok(await vault.GetAvailabilityAsync(ct)));

app.MapGet("/api/vault/availability/{productId:guid}", async Task<IResult> (Guid productId, VaultStore vault, CancellationToken ct) =>
    Results.Ok(new VaultStore.StockAvailabilityRow(productId, await vault.GetProductAvailabilityAsync(productId, ct))));

app.MapGet("/api/vault/stock", async Task<IResult> (VaultStore vault, CancellationToken ct) =>
    Results.Ok(await vault.GetStockSummaryAsync(ct))).RequireAuthorization("AdminOnly");

app.MapPost("/api/vault/keys/import", async Task<IResult> (ImportKeysRequest request, VaultStore vault, CancellationToken ct) =>
{
    if (request.ProductId == Guid.Empty) return Results.BadRequest(new { error = "ProductId is required." });
    if (request.Keys is null || request.Keys.Count == 0 || request.Keys.Count > 1000)
        return Results.BadRequest(new { error = "Provide between 1 and 1000 license keys." });
    try
    {
        var (added, duplicates) = await vault.ImportLinesAsync(request.ProductId, request.Keys, ct);
        return Results.Ok(new { added, duplicates });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization("AdminOnly");

app.Run();

public sealed record ImportKeysRequest(Guid ProductId, IReadOnlyList<string> Keys);
