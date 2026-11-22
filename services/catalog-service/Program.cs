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
builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Database initialization for development.
// Replace EnsureCreated with EF migrations later.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<CatalogDbContext>();

    await db.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
