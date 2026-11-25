using System.Text.Json;
using Contracts.Events;
using RabbitMQ.Client;

public sealed class VaultWorker(IConfiguration config, VaultStore store, ILogger<VaultWorker> log) : BackgroundService
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
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                const string queue = "license.order-paid";
                await channel.ExchangeDeclareAsync(EventRoutes.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
                await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(queue, EventRoutes.Exchange, EventRoutes.OrderPaid, cancellationToken: ct);
                log.LogInformation("License Vault bound to {Queue}", queue);
                while (!ct.IsCancellationRequested)
                {
                    var delivery = await channel.BasicGetAsync(queue, autoAck: false, cancellationToken: ct);
                    if (delivery is null) { await Task.Delay(1000, ct); continue; }
                    OrderPaidEvent? evt;
                    try { evt = JsonSerializer.Deserialize<OrderPaidEvent>(delivery.Body.Span, Json); }
                    catch (JsonException) { evt = null; }
                    if (evt is null || evt.Items is null || evt.Items.Count == 0 || evt.Items.Select(i => i.ItemId).Distinct().Count() != evt.Items.Count)
                    {
                        // No DLX configured yet: leave invalid message unacknowledged and stop this session for inspection.
                        log.LogError("Invalid order.paid message; manual intervention required");
                        throw new InvalidOperationException("Invalid order.paid payload");
                    }
                    if (await store.AllocateAsync(evt, ct) == true)
                    {
                        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
                        log.LogInformation("Keys allocated for order {OrderId}", evt.OrderId);
                    }
                    else
                    {
                        log.LogWarning("Insufficient stock for order {OrderId}; retry after restock", evt.OrderId);
                        await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
                        await Task.Delay(TimeSpan.FromSeconds(15), ct);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Vault consumer failed; retrying"); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
