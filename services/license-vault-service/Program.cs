using Npgsql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args.Length > 0 && args[0] == "import" ? Array.Empty<string>() : args);
var cs = builder.Configuration.GetConnectionString("LicenseDb")
    ?? throw new InvalidOperationException("LicenseDb required");
var key = Convert.FromBase64String(builder.Configuration["LicenseVault:EncryptionKey"] ?? "");
if (key.Length != 32) throw new InvalidOperationException("LicenseVault requires a 32-byte base64 key");
var store = new VaultStore(NpgsqlDataSource.Create(cs), key);
await store.InitializeAsync();
if (args.Length > 0 && args[0] == "import")
{
    if (args.Length != 2 || !Guid.TryParse(args[1], out var product))
        throw new ArgumentException("Usage: import PRODUCT_UUID; one key per stdin line");
    await store.ImportAsync(product);
    return;
}
builder.Services.AddSingleton(store);
builder.Services.AddHostedService<VaultWorker>();
builder.Services.AddHostedService<DeliveryDispatchWorker>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MetadataAddress = builder.Configuration["Keycloak:MetadataAddress"] ?? throw new InvalidOperationException("Keycloak metadata required");
    o.RequireHttpsMetadata = false; // local Keycloak; use HTTPS in production
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidIssuer = builder.Configuration["Keycloak:Issuer"] ?? throw new InvalidOperationException("Keycloak issuer required"),
        ValidateAudience = false // existing realm has no API audience mapper; restrict authorized party below
    };
});
builder.Services.AddAuthorization(o => o.AddPolicy("Library", p => p.RequireAuthenticatedUser().RequireClaim("azp", "marketplace-web").RequireClaim("sub")));
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
app.Run();
