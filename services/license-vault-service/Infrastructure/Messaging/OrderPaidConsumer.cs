using LicenseVault.Api.Application.Services;
using System.Text.Json;
using Contracts.Events;
using LicenseVault.Api.Application.Interfaces;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LicenseVault.Api.Infrastructure.Messaging;

public sealed class OrderPaidConsumer(
    IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopeFactory,
    ILogger<OrderPaidConsumer> log
) : BackgroundService
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var config = options.Value;

        log.LogInformation(
            "OrderPaid Consumer started");

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

                await RabbitMqTopology.DeclareAsync(
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
                        if (args.RoutingKey != EventRoutes.OrderPaid)
                        {
                            throw new InvalidDataException(
                                "Unexpected routing key.");
                        }

                        var message =
                            JsonSerializer.Deserialize<OrderPaidEvent>(
                                args.Body.Span,
                                Json)
                            ?? throw new InvalidDataException(
                                "Empty event payload.");

                        await using var scope =
                            scopeFactory.CreateAsyncScope();

                        var allocation =
                            scope.ServiceProvider
                                .GetRequiredService<
                                    LicenseAllocationService>();

                        await allocation.ProcessAsync(
                            message,
                            stoppingToken);
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
                            "Invalid OrderPaid event rejected: {Error}",
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
                            "Allocation failed: {Error}. Retrying.",
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
                    queue: RabbitMqTopology.OrderPaidQueue,
                    autoAck: false,
                    consumer: consumer,
                    cancellationToken: stoppingToken);

                log.LogInformation(
                    "Listening on queue {Queue}",
                    RabbitMqTopology.OrderPaidQueue);

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
                    "OrderPaid Consumer connection failed: {Error}",
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
}
