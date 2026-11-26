namespace Marketplace.Shared.Messaging;

internal static class OutboxLoop
{
    internal static async Task RunAsync(
        Func<CancellationToken, Task<bool>> processNext,
        ILogger logger,
        CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await processNext(ct))
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
                logger.LogError(
                    ex, "Outbox processing failed");

                await Task.Delay(
                    TimeSpan.FromSeconds(5), ct);
            }
        }
    }
}
