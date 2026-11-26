using Microsoft.EntityFrameworkCore;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Application.Services;

public sealed record WalletBalanceResponse(
    decimal Balance,
    string Currency);

public sealed record WalletTransactionResponse(
    Guid Id,
    string Type,
    decimal Amount,
    decimal BalanceAfter,
    Guid ReferenceId,
    DateTime CreatedAtUtc);

public sealed class WalletService(
    PaymentDbContext db)
{
    public async Task<WalletBalanceResponse> GetBalanceAsync(
        string customerId,
        CancellationToken ct)
    {
        var balance = await db.Wallets
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .Select(w => (decimal?)w.Balance)
            .SingleOrDefaultAsync(ct);

        return new WalletBalanceResponse(
            balance ?? 0m,
            "VND");
    }

    public async Task<IReadOnlyList<WalletTransactionResponse>>
        GetTransactionsAsync(
            string customerId,
            int page,
            int pageSize,
            CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var walletId = await db.Wallets
            .AsNoTracking()
            .Where(w => w.CustomerId == customerId)
            .Select(w => (Guid?)w.Id)
            .SingleOrDefaultAsync(ct);

        if (walletId is null)
            return Array.Empty<WalletTransactionResponse>();

        return await db.WalletTransactions
            .AsNoTracking()
            .Where(t => t.WalletId == walletId.Value)
            .OrderByDescending(t => t.CreatedAtUtc)
            .ThenByDescending(t => t.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(t => new WalletTransactionResponse(
                t.Id,
                t.Type,
                t.Amount,
                t.BalanceAfter,
                t.ReferenceId,
                t.CreatedAtUtc))
            .ToListAsync(ct);
    }
}
