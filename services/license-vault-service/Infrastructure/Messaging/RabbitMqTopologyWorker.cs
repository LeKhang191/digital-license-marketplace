
using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace LicenseVault.Api.Infrastructure.Messaging;

public sealed class RabbitMqTopologyWorker(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqTopologyWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        var config = options.Value;

        log.LogInformation(
            "RabbitMQ topology worker started");

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

                    AutomaticRecoveryEnabled = true,

                    NetworkRecoveryInterval =
                        TimeSpan.FromSeconds(5)
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

                log.LogInformation(
                    "RabbitMQ topology declared successfully");

                log.LogInformation(
                    "Queue ready: {Queue}",
                    RabbitMqTopology.OrderPaidQueue);

                log.LogInformation(
                    "Queue ready: {Queue}",
                    RabbitMqTopology.OrderLicenseEventsQueue);

                while (
                    connection.IsOpen &&
                    channel.IsOpen &&
                    !stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(5),
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
                    ex,
                    "RabbitMQ unavailable. Retrying in 5 seconds");
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
