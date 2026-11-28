using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Refund;
using OrderService.Infrastructure.Workers;
using OrderService.Application.Services;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.License;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Payment;
using OrderService.Infrastructure.Security;

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

builder.Services.AddHttpClient<PaymentClient>(client =>
{
    var url = builder.Configuration["Services:PaymentUrl"]
        ?? "http://payment:8080";

    var key = builder.Configuration["InternalService:Key"]
        ?? throw new InvalidOperationException(
            "Internal service key is missing");

    client.BaseAddress = new Uri(url.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(10);

    client.DefaultRequestHeaders.Add(
        "X-Internal-Service-Key", key);
})
.ConfigurePrimaryHttpMessageHandler(() =>
    new HttpClientHandler { AllowAutoRedirect = false });



builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<RabbitMqPublisher>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddScoped<IOrderManager, OrderManager>();
builder.Services.AddScoped<PartnerOrderService>();
builder.Services.AddHttpClient("PartnerWebhook", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false,
    UseCookies = false
});
builder.Services.AddHostedService<PartnerWebhookWorker>();
builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// Order fulfillment
builder.Services.AddScoped<OrderFulfillmentService>();

builder.Services.AddHostedService<OrderLicenseEventsConsumer>();

builder.Services.AddScoped<OrderPaymentService>();
builder.Services.AddHostedService<OrderPaymentEventsConsumer>();

// Refund orchestration
var refundInternalKey =
    builder.Configuration["InternalService:Key"]
    ?? throw new InvalidOperationException(
        "Internal service key is missing");

foreach (var endpoint in new[]
{
    (
        Name: "RefundLicense",
        Url: builder.Configuration["Services:LicenseUrl"]
             ?? "http://license-vault:8080"
    ),
    (
        Name: "RefundPayment",
        Url: builder.Configuration["Services:PaymentUrl"]
             ?? "http://payment:8080"
    )
})
{
    builder.Services.AddHttpClient(endpoint.Name, client =>
    {
        client.BaseAddress =
            new Uri(endpoint.Url.TrimEnd('/') + "/");

        client.Timeout = TimeSpan.FromSeconds(10);

        client.DefaultRequestHeaders.Add(
            "X-Internal-Service-Key",
            refundInternalKey);
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler
        {
            AllowAutoRedirect = false
        });
}

builder.Services.AddScoped<RefundClient>();
builder.Services.AddScoped<RefundOrchestrator>();
builder.Services.AddHostedService<RefundWorker>();

var app = builder.Build();

// Schema is managed explicitly by EF Core migrations (see docs/DATABASE_MIGRATIONS.md).
// Do not call EnsureCreated() or auto-migrate at application startup.
// This keeps read/write application credentials separate from schema-change operations.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
