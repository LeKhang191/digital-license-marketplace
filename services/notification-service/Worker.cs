using System.Text.Json;
using Contracts.Events;
using RabbitMQ.Client;

namespace Notification.Worker;

public sealed class Worker(IConfiguration config, ILogger<Worker> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = config["RabbitMq:Host"] ?? "rabbitmq",
                    Port = config.GetValue("RabbitMq:Port", 5672),
                    UserName = config["RabbitMq:User"] ?? "guest",
                    Password = config["RabbitMq:Password"] ?? "guest"
                };

                await using var connection = await factory.CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await using var quarantine = await connection.CreateChannelAsync(
                    new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                    stoppingToken);

                const string rejectedQueue = "notification.events.rejected.v1";
                await channel.ExchangeDeclareAsync(EventRoutes.Exchange, ExchangeType.Topic, durable: true, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(rejectedQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);
                await channel.QueueDeclareAsync(EventRoutes.NotificationQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: stoppingToken);

                await channel.QueueBindAsync(EventRoutes.NotificationQueue, EventRoutes.Exchange, EventRoutes.LicenseDelivered, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(EventRoutes.NotificationQueue, EventRoutes.Exchange, EventRoutes.KeyOutOfStock, cancellationToken: stoppingToken);
                await channel.QueueBindAsync(EventRoutes.NotificationQueue, EventRoutes.Exchange, EventRoutes.OrderRefunded, cancellationToken: stoppingToken);

                logger.LogInformation("Notification worker bound to {Queue}", EventRoutes.NotificationQueue);

                while (!stoppingToken.IsCancellationRequested)
                {
                    var delivery = await channel.BasicGetAsync(EventRoutes.NotificationQueue, autoAck: false, cancellationToken: stoppingToken);
                    if (delivery is null)
                    {
                        await Task.Delay(1000, stoppingToken);
                        continue;
                    }

                    var ok = HandleMessage(delivery.RoutingKey, delivery.Body.Span);
                    if (!ok)
                    {
                        await quarantine.BasicPublishAsync(
                            "",
                            rejectedQueue,
                            mandatory: true,
                            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json" },
                            body: delivery.Body,
                            cancellationToken: stoppingToken);
                        logger.LogWarning("Malformed notification event on {RoutingKey} moved to {Queue}", delivery.RoutingKey, rejectedQueue);
                    }

                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Notification worker disconnected; retrying in 5s");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }

    private bool HandleMessage(string routingKey, ReadOnlySpan<byte> body)
    {
        try
        {
            switch (routingKey)
            {
                case EventRoutes.LicenseDelivered:
                    var delivered = JsonSerializer.Deserialize<LicenseDeliveredEvent>(body, Json);
                    if (delivered is null || delivered.OrderId == Guid.Empty) return false;
                    logger.LogInformation(
                        "Notify user {UserId}: order {OrderId} completed ({ItemCount} license item(s) ready in Orders page).",
                        delivered.KeycloakUserId, delivered.OrderId, delivered.Items.Count);
                    return true;

                case EventRoutes.KeyOutOfStock:
                    var oos = JsonSerializer.Deserialize<KeyOutOfStockEvent>(body, Json);
                    if (oos is null || oos.OrderId == Guid.Empty) return false;
                    logger.LogWarning(
                        "Notify admin & customer: order {OrderId} entered REFUNDING because product {ProductId} is out of stock ({Reason}).",
                        oos.OrderId, oos.ProductId, oos.Reason);
                    return true;

                case EventRoutes.OrderRefunded:
                    var refunded = JsonSerializer.Deserialize<OrderRefundedEvent>(body, Json);
                    if (refunded is null || refunded.OrderId == Guid.Empty) return false;
                    logger.LogInformation(
                        "Notify user {UserId}: order {OrderId} refunded ({Amount} VND).",
                        refunded.KeycloakUserId, refunded.OrderId, refunded.Amount);
                    return true;

                default:
                    logger.LogInformation("Ignoring unhandled routing key {RoutingKey}", routingKey);
                    return true;
            }
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
