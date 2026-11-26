using Contracts.Events;
using RabbitMQ.Client;

public sealed class VaultWorker(IConfiguration config, FulfillmentInbox inbox, ILogger<VaultWorker> log) : BackgroundService
{
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
                    var evt = FulfillmentInbox.Parse(delivery.Body);
                    if (evt is null)
                    {
                        await inbox.RejectAsync(delivery.Body, ct);
                        log.LogWarning("Invalid order.paid message quarantined");
                    }
                    else await inbox.AcceptAsync(evt, ct);
                    // ACK only after the durable inbox or quarantine write succeeds.
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken: ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Vault consumer failed; retrying"); }
            await Task.Delay(TimeSpan.FromSeconds(10), ct);
        }
    }
}
