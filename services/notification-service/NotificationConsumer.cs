using System.Net.Mail;
using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Notification.Worker;

public sealed class NotificationConsumer(
    IConfiguration config,
    IServiceScopeFactory scopes,
    ILogger<NotificationConsumer> log
) : BackgroundService
{
    private const string Queue = "notification.order-events";
    private const string DeadQueue = "notification.order-events.dead";
    private const string DeadExchange = "marketplace.events.dead";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = config["RabbitMq:Host"] ?? "rabbitmq",
                    Port = 5672,
                    UserName = config["RabbitMq:User"]!,
                    Password = config["RabbitMq:Password"]!,
                    AutomaticRecoveryEnabled = false
                };

                await using var connection =
                    await factory.CreateConnectionAsync(ct);

                await using var channel =
                    await connection.CreateChannelAsync(
                        cancellationToken: ct);

                await DeclareAsync(channel, ct);

                await channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: 1,
                    global: false,
                    cancellationToken: ct);

                var consumer = new AsyncEventingBasicConsumer(channel);

                consumer.ReceivedAsync += async (_, args) =>
                {
                    try
                    {
                        var payload = args.Body.ToArray();

                        await StoreAsync(
                            args.RoutingKey,
                            payload,
                            ct);

                        await channel.BasicAckAsync(
                            args.DeliveryTag,
                            multiple: false,
                            cancellationToken: ct);
                    }
                    catch (OperationCanceledException)
                        when (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                        when (ex is JsonException
                              or InvalidDataException)
                    {
                        log.LogWarning(
                            "Invalid notification event");

                        await channel.BasicNackAsync(
                            args.DeliveryTag,
                            multiple: false,
                            requeue: false,
                            cancellationToken: ct);
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(
                            "Notification consume failed: {Error}",
                            ex.GetType().Name);

                        await Task.Delay(
                            TimeSpan.FromSeconds(5), ct);

                        await channel.BasicNackAsync(
                            args.DeliveryTag,
                            multiple: false,
                            requeue: true,
                            cancellationToken: ct);
                    }
                };

                await channel.BasicConsumeAsync(
                    queue: Queue,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: ct);

                File.WriteAllText(
                    "/tmp/notification-ready", "ready");

                log.LogInformation(
                    "Listening on {Queue}", Queue);

                while (connection.IsOpen &&
                       channel.IsOpen &&
                       !ct.IsCancellationRequested)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2), ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    "Notification connection failed: {Error}",
                    ex.GetType().Name);
            }
            finally
            {
                File.Delete("/tmp/notification-ready");
            }

            if (!ct.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task StoreAsync(
        string route,
        byte[] body,
        CancellationToken ct)
    {
        Guid orderId;
        string customerId;
        string? email;
        decimal amount;
        bool verified;
        string type;

        switch (route)
        {
            case EventRoutes.OrderCompleted:
            {
                var evt =
                    JsonSerializer.Deserialize<OrderCompletedEvent>(
                        body, Json)
                    ?? throw new InvalidDataException();

                orderId = evt.OrderId;
                customerId = evt.KeycloakUserId;
                email = evt.CustomerEmail;
                amount = evt.Amount;
                verified = evt.CustomerEmailVerified;
                type = "COMPLETED";
                break;
            }

            case EventRoutes.OrderRefunded:
            {
                var evt =
                    JsonSerializer.Deserialize<OrderRefundedEvent>(
                        body, Json)
                    ?? throw new InvalidDataException();

                orderId = evt.OrderId;
                customerId = evt.KeycloakUserId;
                email = evt.CustomerEmail;
                amount = evt.Amount;
                verified = evt.CustomerEmailVerified;
                type = "REFUNDED";
                break;
            }

            default:
                throw new InvalidDataException(
                    "Unsupported notification event");
        }

        if (orderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(customerId) ||
            customerId.Length > 64 ||
            amount <= 0 ||
            amount > 9999999999.99m)
        {
            throw new InvalidDataException(
                "Invalid notification data");
        }

        var enabled =
            config.GetValue<bool>("Smtp:Enabled");

        var validEmail =
            !string.IsNullOrWhiteSpace(email) &&
            email.Length <= 320 &&
            MailAddress.TryCreate(email, out var address) &&
            address.Address == email;

        var status =
            enabled && verified && validEmail
                ? "PENDING"
                : "SKIPPED";

        var recipient =
            status == "PENDING" ? email! : "";

        var reason =
            !verified ? "EMAIL_NOT_VERIFIED"
            : !validEmail ? "INVALID_EMAIL"
            : !enabled ? "SMTP_DISABLED"
            : "";

        var now = DateTime.UtcNow;
        var id = Guid.NewGuid();

        await using var scope =
            scopes.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NotificationDbContext>();

        var inserted =
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO notification_messages (
                    "Id", "OrderId", "CustomerId",
                    "RecipientEmail", "Type", "Amount",
                    "Status", "CreatedAtUtc",
                    "NextRetryAtUtc", "LastError"
                )
                VALUES (
                    {id}, {orderId}, {customerId},
                    {recipient}, {type}, {amount},
                    {status}, {now}, {now},
                    NULLIF({reason}, '')
                )
                ON CONFLICT ("OrderId", "Type")
                DO NOTHING
                """,
                ct);

        log.LogInformation(
            "Notification {OrderId} {Type}: {Result}",
            orderId,
            type,
            inserted == 0 ? "DUPLICATE" : status);
    }

    private static async Task DeclareAsync(
        IChannel channel,
        CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: EventRoutes.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            cancellationToken: ct);

        await channel.ExchangeDeclareAsync(
            exchange: DeadExchange,
            type: ExchangeType.Direct,
            durable: true,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: DeadQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            queue: DeadQueue,
            exchange: DeadExchange,
            routingKey: DeadQueue,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum"
            },
            cancellationToken: ct);

        foreach (var route in new[]
        {
            EventRoutes.OrderCompleted,
            EventRoutes.OrderRefunded
        })
        {
            await channel.QueueBindAsync(
                queue: Queue,
                exchange: EventRoutes.Exchange,
                routingKey: route,
                cancellationToken: ct);
        }
    }
}
