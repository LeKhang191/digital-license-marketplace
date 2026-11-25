using Microsoft.EntityFrameworkCore;
using OrderService.Application.Services;
using OrderService.Infrastructure.Catalog;
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

builder.Services.Configure<VnPayOptions>(
    builder.Configuration.GetSection("VnPay"));
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<VnPayService>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqPublisher>();
builder.Services.AddScoped<IOrderManager, OrderManager>();
builder.Services.AddScoped<VnPayIpnHandler>();
builder.Services.AddHostedService<PaidOrderDispatchWorker>();
builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Dev only. Thay bằng EF migrations sau.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
