using OrderService.Application.Services;

namespace OrderService.Infrastructure.Workers;

public sealed class RefundWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<RefundWorker> log
) : BackgroundService
{
    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        log.LogInformation("Refund Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope =
                    scopeFactory.CreateAsyncScope();

                var service = scope.ServiceProvider
                    .GetRequiredService<RefundOrchestrator>();

                var processed = await service
                    .ProcessNextAsync(stoppingToken);

                if (!processed)
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
                    "Refund processing failed: {Error}",
                    ex.GetType().Name);

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }
    }
}
