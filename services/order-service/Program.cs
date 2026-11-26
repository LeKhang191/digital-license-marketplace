using Microsoft.EntityFrameworkCore;
using OrderService.Application.Services;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.License;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Security;
using OrderService.Infrastructure.VnPay;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("OrderDb")
    ?? throw new InvalidOperationException(
        "OrderDb connection string is missing");

var catalogUrl = builder.Configuration["Services:CatalogUrl"]
    ?? throw new InvalidOperationException(
        "Services:CatalogUrl is missing");

builder.Services.AddDbContext<OrderDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.AddHttpClient<ICatalogClient, CatalogClient>(client =>
{
    client.BaseAddress = new Uri(catalogUrl.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddHttpClient<LicenseClient>(client =>
{
    var licenseUrl = builder.Configuration["Services:LicenseUrl"]
        ?? "http://license-vault:8080";

    client.BaseAddress = new Uri(
        licenseUrl.TrimEnd('/') + "/");

    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.Configure<VnPayOptions>(
    builder.Configuration.GetSection("VnPay"));
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<VnPayService>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddScoped<IOrderManager, OrderManager>();
builder.Services.AddScoped<VnPayIpnHandler>();
builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Order fulfillment
builder.Services.AddScoped<OrderFulfillmentService>();

builder.Services.AddHostedService<OrderLicenseEventsConsumer>();

var app = builder.Build();

// Schema is managed explicitly by EF Core migrations (see docs/DATABASE_MIGRATIONS.md).
// Do not call EnsureCreated() or auto-migrate at application startup.
// This keeps read/write application credentials separate from schema-change operations.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
