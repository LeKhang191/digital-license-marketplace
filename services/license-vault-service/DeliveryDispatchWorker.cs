using System.Text.Json;
using Contracts.Events;
using RabbitMQ.Client;

public sealed class DeliveryDispatchWorker(IConfiguration config, VaultStore store, ILogger<DeliveryDispatchWorker> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory { HostName = config["RabbitMq:Host"] ?? "rabbitmq", UserName = config["RabbitMq:User"] ?? "guest", Password = config["RabbitMq:Password"] ?? "guest" };
                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
                await channel.ExchangeDeclareAsync(EventRoutes.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
                await channel.QueueDeclareAsync(EventRoutes.OrderDeliveryQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(EventRoutes.OrderDeliveryQueue, EventRoutes.Exchange, EventRoutes.LicenseDelivered, cancellationToken: ct);
                while (!ct.IsCancellationRequested)
                {
                    foreach (var evt in await store.PendingDeliveriesAsync(ct))
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        timeout.CancelAfter(TimeSpan.FromSeconds(15));
                        await channel.BasicPublishAsync(EventRoutes.Exchange, EventRoutes.LicenseDelivered, mandatory: true,
                            basicProperties: new BasicProperties { Persistent = true, ContentType = "application/json" },
                            body: JsonSerializer.SerializeToUtf8Bytes(evt, Json), cancellationToken: timeout.Token);
                        await store.MarkDeliverySentAsync(evt.OrderId, ct);
                    }
                    await Task.Delay(2000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Delivery dispatch failed; will retry"); }
            await Task.Delay(5000, ct);
        }
    }
}
