using Microsoft.EntityFrameworkCore;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.Messaging;
using PaymentService.Infrastructure.Security;
using PaymentService.Infrastructure.VnPay;
using PaymentService.Application.Services;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("PaymentDb")
    ?? throw new InvalidOperationException(
        "PaymentDb connection string is missing");

builder.Services.AddDbContext<PaymentDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Services.Configure<VnPayOptions>(
    builder.Configuration.GetSection("VnPay"));

builder.Services.AddSingleton<VnPayService>();

builder.Services.AddScoped<PaymentSessionService>();
builder.Services.AddScoped<WalletService>();
builder.Services.AddScoped<WalletPaymentService>();
builder.Services.AddScoped<RefundService>();

builder.Services.AddSingleton<InternalServiceAuth>();

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection("RabbitMq"));

builder.Services.AddSingleton<IEventPublisher, RabbitMqPublisher>();
builder.Services.AddScoped<VnPayIpnHandler>();
builder.Services.AddHostedService<OutboxWorker>();

builder.Services.AddKeycloakAuth(builder.Configuration);

builder.Services.AddControllers();
builder.Services.AddHealthChecks();

var app = builder.Build();

// Validate internal credentials at startup.
app.Services.GetRequiredService<InternalServiceAuth>();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
