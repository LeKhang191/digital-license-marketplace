using Catalog.Api.Application.Interfaces;
using Catalog.Api.Application.Services;
using Catalog.Api.Infrastructure.Persistence;
using Catalog.Api.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("CatalogDb")
    ?? throw new InvalidOperationException(
        "CatalogDb connection string is missing");

builder.Services.AddDbContext<CatalogDbContext>(
    options => options.UseNpgsql(connectionString)
);

builder.Services.AddScoped<IProductService, ProductService>();
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IgdbMetadataProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(12);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHttpClient<SteamMetadataProvider>(client =>
{
    client.BaseAddress = new Uri("https://store.steampowered.com/");
    client.Timeout = TimeSpan.FromSeconds(15);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Schema is managed explicitly by EF Core migrations (see docs/DATABASE_MIGRATIONS.md).
// Do not call EnsureCreated() or auto-migrate at application startup.
// This keeps read/write application credentials separate from schema-change operations.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
