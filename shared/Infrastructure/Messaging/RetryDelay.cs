namespace Marketplace.Shared.Messaging;

internal static class RetryDelay
{
    internal static int Seconds(int attempt)
        => 1 << Math.Clamp(attempt, 0, 8);
}
