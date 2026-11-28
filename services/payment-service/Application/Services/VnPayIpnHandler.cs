using System.Globalization;
using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.VnPay;

namespace PaymentService.Application.Services;

public sealed record IpnResult(
    string RspCode,
    string Message);

public sealed class VnPayIpnHandler(
    PaymentDbContext db,
    VnPayService vnpay,
    ILogger<VnPayIpnHandler> log)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task<IpnResult> HandleAsync(
        IReadOnlyDictionary<string, string> query,
        CancellationToken ct)
    {
        string Get(string key) =>
            query.TryGetValue(key, out var value)
                ? value : "";

        if (!vnpay.IsConfigured)
            return new("99", "VNPay not configured");

        if (!vnpay.ValidateSignature(query))
            return new("97", "Invalid signature");

        if (!vnpay.ValidateMerchant(Get("vnp_TmnCode")))
            return new("99", "Invalid merchant");

        var reference = Get("vnp_TxnRef");

        if (string.IsNullOrWhiteSpace(reference) ||
            reference.Length > 64)
            return new("01", "Order not found");

        var responseCode = Get("vnp_ResponseCode");
        var transactionStatus = Get("vnp_TransactionStatus");

        if (responseCode.Length != 2 ||
            transactionStatus.Length != 2 ||
            !responseCode.All(char.IsAsciiDigit) ||
            !transactionStatus.All(char.IsAsciiDigit))
            return new("99", "Invalid payment status");

        var success =
            responseCode == "00" &&
            transactionStatus == "00";

        var txNo = Get("vnp_TransactionNo");
        var payDate = Get("vnp_PayDate");

        if (success)
        {
            if (txNo.Length == 0 ||
                txNo.Length > 32 ||
                !txNo.All(char.IsAsciiDigit) ||
                txNo.All(c => c == '0'))
                return new("99", "Invalid transaction number");

            if (!DateTime.TryParseExact(
                    payDate,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
                return new("99", "Invalid payment date");
        }

        if (reference.StartsWith(WalletTopUpService.RefPrefix, StringComparison.Ordinal))
        {
            return await HandleWalletTopUpAsync(
                reference, query, success, txNo, payDate, ct);
        }

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(ct);

            var rows = await db.Payments
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM payments
                    WHERE "PaymentRef" = {reference}
                    FOR UPDATE
                    """)
                .ToListAsync(ct);

            var payment = rows.SingleOrDefault();

            if (payment is null)
                return new("01", "Order not found");

            if (payment.PaymentMethod != "VNPAY")
            {
                log.LogWarning(
                    "VNPay callback rejected for non-VNPay payment");

                return new("99", "Invalid payment method");
            }


            if (!long.TryParse(
                    Get("vnp_Amount"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var amount) ||
                amount != VnPayService.ToVnpAmount(
                    payment.Amount))
                return new("04", "Invalid amount");

            var recoverable =
                success &&
                payment.Status == PaymentStatus.Failed &&
                payment.PaidAtUtc == null &&
                string.IsNullOrEmpty(payment.VnpTransactionNo);

            if (payment.Status != PaymentStatus.Pending &&
                !recoverable)
            {
                if (success &&
                    !string.IsNullOrEmpty(payment.VnpTransactionNo) &&
                    payment.VnpTransactionNo != txNo)
                {
                    log.LogWarning(
                        "Payment {PaymentId} requires reconciliation",
                        payment.Id);

                    return new("99", "Payment reconciliation required");
                }

                return new("02", "Order already confirmed");
            }

            if (success)
            {
                var lockKey = $"vnpay:transaction:{txNo}";

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    SELECT pg_advisory_xact_lock(
                        hashtextextended({lockKey}, 0)
                    )
                    """, ct);

                var duplicate = await db.Payments.AnyAsync(
                    p => p.Id != payment.Id &&
                         p.VnpTransactionNo == txNo,
                    ct) || await db.WalletTopUps.AnyAsync(
                    t => t.VnpTransactionNo == txNo,
                    ct);

                if (duplicate)
                {
                    log.LogWarning(
                        "Duplicate provider transaction detected");

                    return new("99", "Payment reconciliation required");
                }
            }

            var now = DateTime.UtcNow;
            string routingKey;
            string payload;

            if (success)
            {
                payment.Status = PaymentStatus.Succeeded;
                payment.VnpTransactionNo = txNo;
                payment.VnpPayDate = payDate;
                payment.PaidAtUtc = now;

                routingKey = EventRoutes.PaymentSucceeded;

                payload = JsonSerializer.Serialize(
                    new PaymentSucceededEvent(
                        payment.Id,
                        payment.OrderId,
                        payment.CustomerId,
                        payment.Amount,
                        payment.PaymentRef,
                        txNo,
                        payDate,
                        now),
                    Json);
            }
            else
            {
                payment.Status = PaymentStatus.Failed;

                routingKey = EventRoutes.PaymentFailed;

                payload = JsonSerializer.Serialize(
                    new PaymentFailedEvent(
                        payment.Id,
                        payment.OrderId,
                        payment.CustomerId,
                        payment.PaymentRef,
                        responseCode,
                        transactionStatus,
                        now),
                    Json);
            }

            db.OutboxMessages.Add(new OutboxMessage
            {
                AggregateId = payment.Id,
                RoutingKey = routingKey,
                Payload = payload,
                CreatedAtUtc = now,
                NextRetryAtUtc = now
            });

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            log.LogInformation(
                "Payment {PaymentId}: {Status}",
                payment.Id,
                payment.Status);

            return new("00", "Confirm Success");
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Payment IPN transaction failed");

            return new("99", "Unknown error");
        }
    }

    private async Task<IpnResult> HandleWalletTopUpAsync(
        string reference,
        IReadOnlyDictionary<string, string> query,
        bool success,
        string txNo,
        string payDate,
        CancellationToken ct)
    {
        string Get(string key) =>
            query.TryGetValue(key, out var value)
                ? value : "";

        try
        {
            await using var transaction =
                await db.Database.BeginTransactionAsync(ct);

            var rows = await db.WalletTopUps
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM wallet_topups
                    WHERE "PaymentRef" = {reference}
                    FOR UPDATE
                    """)
                .ToListAsync(ct);

            var topUp = rows.SingleOrDefault();

            if (topUp is null)
                return new("01", "Order not found");

            if (!long.TryParse(
                    Get("vnp_Amount"),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var amount) ||
                amount != VnPayService.ToVnpAmount(topUp.Amount))
            {
                return new("04", "Invalid amount");
            }

            var recoverable =
                success &&
                topUp.Status == PaymentStatus.Failed &&
                topUp.PaidAtUtc == null &&
                string.IsNullOrEmpty(topUp.VnpTransactionNo);

            if (topUp.Status != PaymentStatus.Pending &&
                !recoverable)
            {
                if (success &&
                    !string.IsNullOrEmpty(topUp.VnpTransactionNo) &&
                    topUp.VnpTransactionNo != txNo)
                {
                    log.LogWarning(
                        "WalletTopUp {TopUpId} requires reconciliation",
                        topUp.Id);

                    return new("99", "Payment reconciliation required");
                }

                return new("02", "Order already confirmed");
            }

            var now = DateTime.UtcNow;

            if (success)
            {
                var lockKey = $"vnpay:transaction:{txNo}";

                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    SELECT pg_advisory_xact_lock(
                        hashtextextended({lockKey}, 0)
                    )
                    """, ct);

                var duplicate = await db.Payments.AnyAsync(
                    p => p.VnpTransactionNo == txNo, ct) ||
                    await db.WalletTopUps.AnyAsync(
                        t => t.Id != topUp.Id && t.VnpTransactionNo == txNo, ct);

                if (duplicate)
                {
                    log.LogWarning(
                        "Duplicate provider transaction detected for wallet top-up");

                    return new("99", "Payment reconciliation required");
                }

                topUp.Status = PaymentStatus.Succeeded;
                topUp.VnpTransactionNo = txNo;
                topUp.VnpPayDate = payDate;
                topUp.PaidAtUtc = now;

                var newWalletId = Guid.NewGuid();
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO wallets (
                        "Id", "CustomerId", "Balance",
                        "CreatedAtUtc", "UpdatedAtUtc"
                    )
                    VALUES (
                        {newWalletId}, {topUp.CustomerId}, 0, {now}, {now}
                    )
                    ON CONFLICT ("CustomerId") DO NOTHING
                    """, ct);

                var walletRows = await db.Wallets
                    .FromSqlInterpolated($"""
                        SELECT *
                        FROM wallets
                        WHERE "CustomerId" = {topUp.CustomerId}
                        FOR UPDATE
                        """)
                    .ToListAsync(ct);

                var wallet = walletRows.Single();
                wallet.Balance += topUp.Amount;
                wallet.UpdatedAtUtc = now;

                db.WalletTransactions.Add(new WalletTransaction
                {
                    WalletId = wallet.Id,
                    Type = WalletTransactionType.TopUp,
                    Amount = topUp.Amount,
                    BalanceAfter = wallet.Balance,
                    ReferenceId = topUp.Id,
                    CreatedAtUtc = now
                });
            }
            else
            {
                topUp.Status = PaymentStatus.Failed;
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            log.LogInformation(
                "WalletTopUp {TopUpId}: {Status}",
                topUp.Id,
                topUp.Status);

            return new("00", "Confirm Success");
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Wallet top-up IPN transaction failed");

            return new("99", "Unknown error");
        }
    }
}
