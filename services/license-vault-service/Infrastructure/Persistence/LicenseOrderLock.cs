using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Persistence;

public static class LicenseOrderLock
{
    public static async Task AcquireAsync(
        LicenseDbContext db,
        Guid orderId,
        CancellationToken ct)
    {
        if (orderId == Guid.Empty)
            throw new ArgumentException(
                "Order ID is required.");

        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "An active transaction is required.");

        var lockKey = $"allocation:{orderId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """,
            ct);
    }
}
