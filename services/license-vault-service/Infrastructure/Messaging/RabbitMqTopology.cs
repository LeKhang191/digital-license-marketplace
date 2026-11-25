
using Contracts.Events;
using RabbitMQ.Client;

namespace LicenseVault.Api.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public const string OrderPaidQueue =
        "license-vault.order-paid";

    public const string OrderPaidDeadLetterQueue =
        "license-vault.order-paid.dead";

    public const string DeadLetterExchange =
        "marketplace.events.dead";

    public const string DeadLetterRoutingKey =
        "license-vault.order-paid.dead";

    public const string OrderLicenseEventsQueue =
        "order.license-events";

    public static async Task DeclareAsync(
        IChannel channel,
        string exchange,
        CancellationToken ct)
    {
        // Main exchange
        await channel.ExchangeDeclareAsync(
            exchange: exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Dead-letter exchange
        await channel.ExchangeDeclareAsync(
            exchange: DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: ct);

        // Dead-letter queue
        await channel.QueueDeclareAsync(
            queue: OrderPaidDeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: OrderPaidDeadLetterQueue,
            exchange: DeadLetterExchange,
            routingKey: DeadLetterRoutingKey,
            cancellationToken: ct);

        // License Vault queue
        await channel.QueueDeclareAsync(
            queue: OrderPaidQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] =
                    DeadLetterExchange,

                ["x-dead-letter-routing-key"] =
                    DeadLetterRoutingKey
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: OrderPaidQueue,
            exchange: exchange,
            routingKey: EventRoutes.OrderPaid,
            cancellationToken: ct);

        // Order Service queue
        await channel.QueueDeclareAsync(
            queue: OrderLicenseEventsQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum"
            },
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: OrderLicenseEventsQueue,
            exchange: exchange,
            routingKey: EventRoutes.KeyAssigned,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: OrderLicenseEventsQueue,
            exchange: exchange,
            routingKey: EventRoutes.KeyOutOfStock,
            cancellationToken: ct);
    }
}
