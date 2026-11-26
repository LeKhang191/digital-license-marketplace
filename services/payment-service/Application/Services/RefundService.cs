using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;

namespace PaymentService.Application.Services;

public sealed record WalletRefundRequest(
    Guid RequestId,
    Guid OrderId,
    string CustomerId,
    string PaymentRef,
    string Reason,
    string RequestedBy);

public sealed record WalletRefundResponse(
    Guid RefundId,
    Guid RequestId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    string Status,
    DateTime? CompletedAtUtc);

public sealed class RefundService(PaymentDbContext db)
{
    private static WalletRefundResponse Result(
        Refund refund, Payment payment)
        => new(
            refund.Id,
            refund.RequestId,
            payment.Id,
            payment.OrderId,
            refund.Amount,
            refund.Status,
            refund.CompletedAtUtc);

    public async Task<(
        WalletRefundResponse? Refund,
        string? Error)> RefundToWalletAsync(
        WalletRefundRequest request,
        CancellationToken ct)
    {
        if (request.RequestId == Guid.Empty ||
            request.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            request.CustomerId.Length > 64 ||
            string.IsNullOrWhiteSpace(request.PaymentRef) ||
            request.PaymentRef.Length > 64 ||
            string.IsNullOrWhiteSpace(request.Reason) ||
            request.Reason.Length > 500 ||
            string.IsNullOrWhiteSpace(request.RequestedBy) ||
            request.RequestedBy.Length > 64)
        {
            return (null, "INVALID_REFUND_REQUEST");
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var requestLock = $"refund:{request.RequestId:N}";
        var paymentLock = $"payment:{request.OrderId:N}";

        foreach (var lockKey in new[] {
            requestLock, paymentLock
        })
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                SELECT pg_advisory_xact_lock(
                    hashtextextended({lockKey}, 0)
                )
                """, ct);
        }

        var rows = await db.Payments
            .FromSqlInterpolated($"""
                SELECT *
                FROM payments
                WHERE "OrderId" = {request.OrderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var payment = rows.SingleOrDefault();

        if (payment is null)
            return (null, "PAYMENT_NOT_FOUND");

        if (payment.CustomerId != request.CustomerId ||
            payment.PaymentRef != request.PaymentRef)
        {
            return (null, "REFUND_DATA_CONFLICT");
        }

        var previous = await db.Refunds
            .SingleOrDefaultAsync(
                r => r.RequestId == request.RequestId, ct);

        if (previous is not null)
        {
            if (previous.PaymentId != payment.Id ||
                previous.RequestedBy != request.RequestedBy ||
                previous.Reason != request.Reason)
            {
                return (null, "REFUND_DATA_CONFLICT");
            }

            if (previous.Status == RefundStatus.Succeeded &&
                payment.Status == PaymentStatus.Refunded)
            {
                await tx.CommitAsync(ct);
                return (Result(previous, payment), null);
            }

            return (null, "REFUND_NOT_PAYABLE");
        }

        if (payment.Status == PaymentStatus.Refunded)
            return (null, "PAYMENT_ALREADY_REFUNDED");

        if (payment.Status != PaymentStatus.Succeeded ||
            payment.PaidAtUtc is null ||
            payment.Amount <= 0)
        {
            return (null, "PAYMENT_NOT_REFUNDABLE");
        }

        if (await db.Refunds.AnyAsync(
            r => r.PaymentId == payment.Id &&
                 (r.Status == RefundStatus.Pending ||
                  r.Status == RefundStatus.Processing ||
                  r.Status == RefundStatus.Unknown ||
                  r.Status == RefundStatus.Succeeded), ct))
        {
            return (null, "REFUND_ALREADY_EXISTS");
        }

        var now = DateTime.UtcNow;
        var walletId = Guid.NewGuid();

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO wallets (
                "Id", "CustomerId", "Balance",
                "CreatedAtUtc", "UpdatedAtUtc"
            )
            VALUES (
                {walletId}, {payment.CustomerId},
                0, {now}, {now}
            )
            ON CONFLICT ("CustomerId") DO NOTHING
            """, ct);

        var wallets = await db.Wallets
            .FromSqlInterpolated($"""
                SELECT *
                FROM wallets
                WHERE "CustomerId" = {payment.CustomerId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var wallet = wallets.Single();

        if (wallet.Balance >
            9999999999999999.99m - payment.Amount)
        {
            return (null, "WALLET_BALANCE_LIMIT");
        }

        var refund = new Refund
        {
            PaymentId = payment.Id,
            RequestId = request.RequestId,
            Amount = payment.Amount,
            Reason = request.Reason,
            RequestedBy = request.RequestedBy,
            Status = RefundStatus.Succeeded,
            CreatedAtUtc = now,
            CompletedAtUtc = now
        };

        wallet.Balance += payment.Amount;
        wallet.UpdatedAtUtc = now;

        payment.Status = PaymentStatus.Refunded;

        db.Refunds.Add(refund);

        db.WalletTransactions.Add(new WalletTransaction
        {
            WalletId = wallet.Id,
            Type = WalletTransactionType.Refund,
            Amount = refund.Amount,
            BalanceAfter = wallet.Balance,
            ReferenceId = refund.Id,
            CreatedAtUtc = now
        });

        var evt = new PaymentRefundedEvent(
            refund.Id,
            payment.Id,
            payment.OrderId,
            refund.Amount,
            now,
            refund.RequestId,
            payment.CustomerId,
            payment.PaymentRef);

        db.OutboxMessages.Add(new OutboxMessage
        {
            AggregateId = payment.Id,
            RoutingKey = EventRoutes.PaymentRefunded,
            Payload = JsonSerializer.Serialize(
                evt,
                new JsonSerializerOptions(
                    JsonSerializerDefaults.Web)),
            CreatedAtUtc = now,
            NextRetryAtUtc = now
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (Result(refund, payment), null);
    }
}
