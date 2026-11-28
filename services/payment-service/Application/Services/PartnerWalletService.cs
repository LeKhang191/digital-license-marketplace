using Marketplace.Shared.Security;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Application.Services;

public sealed record PartnerTopUpResponse(
    Guid TopUpId,
    string PartnerId,
    string IdempotencyKey,
    decimal Amount,
    decimal BalanceAfter,
    string Currency,
    string? Note,
    string RequestedBy,
    Guid WalletTransactionId,
    DateTime CreatedAtUtc,
    bool AlreadyProcessed);

public sealed class PartnerWalletService(
    PaymentDbContext db)
{
    public const decimal MaxSingleTopUp = 1_000_000_000m;

    public static bool IsValidIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length < 8 || trimmed.Length > 64)
            return false;

        foreach (var ch in trimmed)
        {
            if (ch < 0x21 || ch > 0x7E)
                return false;
        }

        return true;
    }

    public async Task<(PartnerTopUpResponse? Result, string? Error)> TopUpAsync(
        string partnerId,
        string idempotencyKey,
        decimal amount,
        string? note,
        string auditorId,
        CancellationToken ct)
    {
        if (!KeycloakAuthentication.IsValidPartnerId(partnerId))
            return (null, "INVALID_PARTNER_ID");

        if (!IsValidIdempotencyKey(idempotencyKey))
            return (null, "INVALID_IDEMPOTENCY_KEY");

        if (amount <= 0 ||
            amount > MaxSingleTopUp ||
            decimal.Round(amount, 2) != amount)
        {
            return (null, "INVALID_AMOUNT");
        }

        var normalizedNote = string.IsNullOrWhiteSpace(note)
            ? null
            : note.Trim();

        if (normalizedNote is not null && normalizedNote.Length > 500)
            return (null, "INVALID_NOTE");

        if (string.IsNullOrWhiteSpace(auditorId) || auditorId.Length > 64)
            return (null, "INVALID_AUDITOR");

        var key = idempotencyKey.Trim();
        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var lockKey = $"partner-topup:{partnerId}";
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """,
            ct);

        var existing = await db.PartnerWalletTopUps
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.PartnerId == partnerId && x.IdempotencyKey == key,
                ct);

        if (existing is not null)
        {
            if (existing.Amount != amount ||
                !string.Equals(existing.Note, normalizedNote, StringComparison.Ordinal))
            {
                return (null, "IDEMPOTENCY_CONFLICT");
            }

            await tx.CommitAsync(ct);

            return (ToResponse(existing, alreadyProcessed: true), null);
        }

        var wallets = await db.Wallets
            .FromSqlInterpolated($"""
                SELECT *
                FROM wallets
                WHERE "CustomerId" = {subject}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var now = DateTime.UtcNow;
        var wallet = wallets.SingleOrDefault();

        if (wallet is null)
        {
            wallet = new Wallet
            {
                CustomerId = subject,
                Balance = 0m,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };

            db.Wallets.Add(wallet);
            await db.SaveChangesAsync(ct);
        }

        wallet.Balance += amount;
        wallet.UpdatedAtUtc = now;

        var topUpId = Guid.NewGuid();

        var ledgerEntry = new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.TopUp,
            Amount = amount,
            BalanceAfter = wallet.Balance,
            ReferenceId = topUpId,
            CreatedAtUtc = now
        };

        db.WalletTransactions.Add(ledgerEntry);

        var topUp = new PartnerWalletTopUp
        {
            Id = topUpId,
            PartnerId = partnerId,
            IdempotencyKey = key,
            Amount = amount,
            BalanceAfter = wallet.Balance,
            Note = normalizedNote,
            RequestedBy = auditorId,
            WalletTransactionId = ledgerEntry.Id,
            CreatedAtUtc = now
        };

        db.PartnerWalletTopUps.Add(topUp);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (ToResponse(topUp, alreadyProcessed: false), null);
    }

    public async Task<IReadOnlyList<PartnerTopUpResponse>> ListTopUpsAsync(
        string partnerId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        page = Math.Clamp(page, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var rows = await db.PartnerWalletTopUps
            .AsNoTracking()
            .Where(x => x.PartnerId == partnerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return rows.Select(x => ToResponse(x, alreadyProcessed: false)).ToList();
    }

    private static PartnerTopUpResponse ToResponse(
        PartnerWalletTopUp row,
        bool alreadyProcessed) => new(
        row.Id,
        row.PartnerId,
        row.IdempotencyKey,
        row.Amount,
        row.BalanceAfter,
        "VND",
        row.Note,
        row.RequestedBy,
        row.WalletTransactionId,
        row.CreatedAtUtc,
        alreadyProcessed);
}
