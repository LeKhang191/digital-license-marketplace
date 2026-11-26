using Microsoft.EntityFrameworkCore;
using Notification.Worker;

File.Delete("/tmp/notification-ready");

var builder = Host.CreateApplicationBuilder(args);

var connectionString = builder.Configuration
    .GetConnectionString("NotificationDb")
    ?? throw new InvalidOperationException(
        "NotificationDb connection is missing");

builder.Services.AddDbContext<NotificationDbContext>(
    options => options.UseNpgsql(connectionString));

builder.Logging.AddFilter(
    "Microsoft.EntityFrameworkCore.Database.Command",
    LogLevel.Warning);

builder.Services.AddSingleton<EmailSender>();

builder.Services.AddHostedService<NotificationConsumer>();
builder.Services.AddHostedService<EmailWorker>();

using var host = builder.Build();

await using (var scope = host.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<NotificationDbContext>();

    await db.Messages.AsNoTracking().AnyAsync();
}

await host.RunAsync();
