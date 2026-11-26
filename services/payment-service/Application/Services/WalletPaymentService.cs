using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Application.Services;

public sealed record WalletPaymentResponse(
    Guid PaymentId,
    Guid OrderId,
    string PaymentRef,
    string Status);

public sealed class WalletPaymentService(
    PaymentDbContext db)
{
    public async Task<(
        WalletPaymentResponse? Payment,
        string? Error)> FindExistingAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        var payment = await db.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == request.OrderId, ct);

        if (payment is null)
            return (null, "PAYMENT_NOT_FOUND");

        if (payment.CustomerId != request.CustomerId ||
            payment.Amount != request.Amount ||
            payment.PaymentMethod != "WALLET" ||
            payment.PaymentRef != $"W{request.OrderId:N}")
        {
            return (null, "PAYMENT_DATA_CONFLICT");
        }

        if (payment.Status != PaymentStatus.Succeeded)
            return (null, "PAYMENT_NOT_PAYABLE");

        return (new WalletPaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.PaymentRef,
            payment.Status), null);
    }

    public async Task<(
        WalletPaymentResponse? Payment,
        string? Error)> PayAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        if (request.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            request.CustomerId.Length > 64 ||
            request.Amount <= 0 ||
            request.Amount > 9999999999.99m ||
            decimal.Round(request.Amount, 2) != request.Amount ||
            request.OrderCreatedAtUtc.Kind != DateTimeKind.Utc ||
            request.OrderCreatedAtUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return (null, "INVALID_PAYMENT_REQUEST");
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var lockKey = $"payment:{request.OrderId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """, ct);

        var payments = await db.Payments
            .FromSqlInterpolated($"""
                SELECT *
                FROM payments
                WHERE "OrderId" = {request.OrderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var payment = payments.SingleOrDefault();

        var reference = $"W{request.OrderId:N}";

        if (payment is not null)
        {
            if (payment.CustomerId != request.CustomerId ||
                payment.Amount != request.Amount ||
                payment.PaymentMethod != "WALLET" ||
                payment.PaymentRef != reference)
            {
                return (null, "PAYMENT_DATA_CONFLICT");
            }

            // Retry after a successful transaction.
            if (payment.Status == PaymentStatus.Succeeded)
            {
                await tx.CommitAsync(ct);

                return (new WalletPaymentResponse(
                    payment.Id,
                    payment.OrderId,
                    payment.PaymentRef,
                    payment.Status), null);
            }

            return (null, "PAYMENT_NOT_PAYABLE");
        }

        var expires = request.OrderCreatedAtUtc.AddMinutes(15);

        if (DateTime.UtcNow >= expires)
            return (null, "PAYMENT_EXPIRED");

        var wallets = await db.Wallets
            .FromSqlInterpolated($"""
                SELECT *
                FROM wallets
                WHERE "CustomerId" = {request.CustomerId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var wallet = wallets.SingleOrDefault();

        if (wallet is null || wallet.Balance < request.Amount)
            return (null, "INSUFFICIENT_WALLET_BALANCE");

        var now = DateTime.UtcNow;

        if (now >= expires)
            return (null, "PAYMENT_EXPIRED");

        payment = new Payment
        {
            OrderId = request.OrderId,
            CustomerId = request.CustomerId,
            Amount = request.Amount,
            PaymentMethod = "WALLET",
            PaymentRef = reference,
            Status = PaymentStatus.Succeeded,
            CreatedAtUtc = request.OrderCreatedAtUtc,
            ExpiresAtUtc = expires,
            PaidAtUtc = now
        };

        wallet.Balance -= payment.Amount;
        wallet.UpdatedAtUtc = now;

        db.Payments.Add(payment);

        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.Purchase,
            Amount = payment.Amount,
            BalanceAfter = wallet.Balance,
            ReferenceId = payment.Id,
            CreatedAtUtc = now
        });

        var evt = new PaymentSucceededEvent(
            payment.Id,
            payment.OrderId,
            payment.CustomerId,
            payment.Amount,
            payment.PaymentRef,
            null,
            null,
            now,
            "WALLET");

        db.OutboxMessages.Add(new OutboxMessage
        {
            AggregateId = payment.Id,
            RoutingKey = EventRoutes.PaymentSucceeded,
            Payload = JsonSerializer.Serialize(
                evt,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web)),
            CreatedAtUtc = now,
            NextRetryAtUtc = now
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (new WalletPaymentResponse(
            payment.Id,
            payment.OrderId,
            payment.PaymentRef,
            payment.Status), null);
    }
}
