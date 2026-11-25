using System.Text.Json;
using Contracts.Events;
using Microsoft.Extensions.Options;
using OrderService.Application.Services;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace OrderService.Infrastructure.Messaging;

public sealed class OrderLicenseEventsConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderLicenseEventsConsumer> log
) : BackgroundService
{
    private const string Queue =
        "order.license-events";

    private const string DeadQueue =
        "order.license-events.dead";

    private const string DeadExchange =
        "marketplace.events.dead";

    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Order License Events Consumer started");

        var config = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory
                {
                    HostName = config.Host,
                    Port = config.Port,
                    UserName = config.User,
                    Password = config.Password,
                    AutomaticRecoveryEnabled = false
                };

                await using var connection =
                    await factory.CreateConnectionAsync(
                        stoppingToken);

                await using var channel =
                    await connection.CreateChannelAsync(
                        cancellationToken: stoppingToken);

                await DeclareAsync(
                    channel,
                    config.Exchange,
                    stoppingToken);

                await channel.BasicQosAsync(
                    prefetchSize: 0,
                    prefetchCount: 1,
                    global: false,
                    cancellationToken: stoppingToken);

                var consumer =
                    new AsyncEventingBasicConsumer(channel);

                consumer.ReceivedAsync += async (_, args) =>
                {
                    try
                    {
                        await using var scope =
                            scopeFactory.CreateAsyncScope();

                        var service = scope.ServiceProvider
                            .GetRequiredService<
                                OrderFulfillmentService>();

                        switch (args.RoutingKey)
                        {
                            case EventRoutes.KeyAssigned:
                            {
                                var evt =
                                    JsonSerializer.Deserialize<
                                        KeyAssignedEvent>(
                                        args.Body.Span, Json)
                                    ?? throw new InvalidDataException(
                                        "Empty assigned event.");

                                await service.ProcessAssignedAsync(
                                    evt,
                                    stoppingToken);

                                break;
                            }

                            case EventRoutes.KeyOutOfStock:
                            {
                                var evt =
                                    JsonSerializer.Deserialize<
                                        KeyOutOfStockEvent>(
                                        args.Body.Span, Json)
                                    ?? throw new InvalidDataException(
                                        "Empty out-of-stock event.");

                                await service.ProcessOutOfStockAsync(
                                    evt,
                                    stoppingToken);

                                break;
                            }

                            default:
                                throw new InvalidDataException(
                                    "Unexpected routing key.");
                        }
                    }
                    catch (OperationCanceledException)
                        when (stoppingToken.IsCancellationRequested)
                    {
                        return;
                    }
                    catch (Exception ex)
                        when (ex is JsonException
                              or InvalidDataException)
                    {
                        log.LogWarning(
                            "Invalid fulfillment event: {Error}",
                            ex.GetType().Name);

                        await channel.BasicNackAsync(
                            deliveryTag: args.DeliveryTag,
                            multiple: false,
                            requeue: false,
                            cancellationToken: stoppingToken);

                        return;
                    }
                    catch (Exception ex)
                    {
                        log.LogWarning(
                            "Fulfillment processing failed: {Error}",
                            ex.GetType().Name);

                        await Task.Delay(
                            TimeSpan.FromSeconds(5),
                            stoppingToken);

                        await channel.BasicNackAsync(
                            deliveryTag: args.DeliveryTag,
                            multiple: false,
                            requeue: true,
                            cancellationToken: stoppingToken);

                        return;
                    }

                    await channel.BasicAckAsync(
                        deliveryTag: args.DeliveryTag,
                        multiple: false,
                        cancellationToken: stoppingToken);
                };

                await channel.BasicConsumeAsync(
                    queue: Queue,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                log.LogInformation(
                    "Listening on queue {Queue}",
                    Queue);

                while (
                    connection.IsOpen &&
                    channel.IsOpen &&
                    !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    "Order Consumer connection failed: {Error}",
                    ex.GetType().Name);
            }

            if (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }
    }

    private static async Task DeclareAsync(
        IChannel channel,
        string exchange,
        CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: exchange,
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
            EventRoutes.KeyAssigned,
            EventRoutes.KeyOutOfStock
        })
        {
            await channel.QueueBindAsync(
                queue: Queue,
                exchange: exchange,
                routingKey: route,
                cancellationToken: ct);
        }
    }
}
