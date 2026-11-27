using Marketplace.Shared.Security;
using LicenseVault.Api.Infrastructure.Kinguin;

using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Application.Services;
using LicenseVault.Api.Infrastructure.Catalog;
using LicenseVault.Api.Infrastructure.Persistence;
using LicenseVault.Api.Infrastructure.Security;
using LicenseVault.Api.Infrastructure.Messaging;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("LicenseDb")
    ?? throw new InvalidOperationException(
        "LicenseDb connection string is missing");

var catalogUrl = builder.Configuration[
    "Services:CatalogUrl"]
    ?? throw new InvalidOperationException(
        "Services:CatalogUrl is missing");

// Database
builder.Services.AddDbContext<LicenseDbContext>(
    options => options.UseNpgsql(connectionString));

// Catalog integration
builder.Services.AddHttpClient<
    ICatalogClient,
    CatalogClient>(client =>
{
    client.BaseAddress = new Uri(
        catalogUrl.TrimEnd('/') + "/");

    client.Timeout = TimeSpan.FromSeconds(5);
});

// Supplier quotes only; no purchase or fulfillment capability.
builder.Services.AddHttpClient<KinguinCatalog>(client =>
{
    client.BaseAddress = new Uri("https://gateway.sandbox.kinguin.net/esa/");
    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AllowAutoRedirect = false
});

// Customer order verification
builder.Services.AddHttpClient("LicenseOrders", client =>
{
    client.BaseAddress = new Uri(
        (builder.Configuration["Services:OrderUrl"]
        ?? "http://order:8080").TrimEnd('/') + "/");

    client.Timeout = TimeSpan.FromSeconds(5);
}).ConfigurePrimaryHttpMessageHandler(
    () => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

// Support ticket eligibility
builder.Services.AddHttpClient("LicenseSupport", client =>
{
    client.BaseAddress = new Uri(
        (builder.Configuration["Services:SupportUrl"]
        ?? "http://support:8080").TrimEnd('/') + "/");

    client.Timeout = TimeSpan.FromSeconds(10);
}).ConfigurePrimaryHttpMessageHandler(
    () => new HttpClientHandler
    {
        AllowAutoRedirect = false
    });

// Encryption
builder.Services.Configure<LicenseEncryptionOptions>(
    builder.Configuration.GetSection("LicenseEncryption"));

builder.Services.AddSingleton<
    ILicenseEncryption,
    LicenseEncryption>();

// Business services
builder.Services.AddScoped<
    ILicenseService,
    LicenseService>();

// RabbitMQ
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddHostedService<RabbitMqTopologyWorker>();

// Security
builder.Services.AddKeycloakAuth(
    builder.Configuration);

// HTTP services
builder.Services.AddControllers();
builder.Services.AddHealthChecks();

// License allocation
builder.Services.AddScoped<LicenseAllocationService>();
builder.Services.AddScoped<LicenseRefundService>();
builder.Services.AddSingleton<InternalServiceAuth>();

// Enable the consumer explicitly when ready.
builder.Services.AddHostedService<OrderPaidConsumer>();

// Allocation result publisher
builder.Services.AddSingleton<RabbitMqPublisher>();

builder.Services.AddHostedService<AllocationOutboxWorker>();
builder.Services.AddHostedService<KinguinMockWorkflowWorker>();

var app = builder.Build();

_ = app.Services.GetRequiredService<InternalServiceAuth>();

// Validate encryption configuration at startup
_ = app.Services.GetRequiredService<ILicenseEncryption>();

// Database schema is managed by EF Core migrations.

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
