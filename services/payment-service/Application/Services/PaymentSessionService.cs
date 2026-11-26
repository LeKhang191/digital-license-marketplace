using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PaymentService.Domain.Entities;
using PaymentService.Infrastructure.Persistence;
using PaymentService.Infrastructure.VnPay;

namespace PaymentService.Application.Services;

public sealed record PaymentSessionRequest(
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    DateTime OrderCreatedAtUtc,
    string ClientIp);

public sealed record PaymentSessionResponse(
    Guid PaymentId,
    Guid OrderId,
    string PaymentRef,
    string Status,
    DateTime ExpiresAtUtc,
    string PaymentUrl);

public sealed class PaymentSessionService(
    PaymentDbContext db,
    VnPayService vnpay,
    IOptions<VnPayOptions> options)
{
    public async Task<(
        PaymentSessionResponse? Session,
        string? Error)> CreateAsync(
        PaymentSessionRequest request,
        CancellationToken ct)
    {
        if (!vnpay.IsConfigured)
            return (null, "PAYMENT_NOT_CONFIGURED");

        if (request.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            request.CustomerId.Length > 64 ||
            !System.Net.IPAddress.TryParse(request.ClientIp, out _) ||
            decimal.Round(request.Amount, 2) != request.Amount ||
            request.Amount <= 0 ||
            request.Amount > 9999999999.99m ||
            request.OrderCreatedAtUtc.Kind != DateTimeKind.Utc ||
            request.OrderCreatedAtUtc > DateTime.UtcNow.AddMinutes(1))
        {
            return (null, "INVALID_PAYMENT_REQUEST");
        }

        var minutes = options.Value.ExpireMinutes;

        if (minutes <= 0 || minutes > 1440)
            return (null, "INVALID_PAYMENT_CONFIGURATION");

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var lockKey = $"payment:{request.OrderId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """,
            ct);

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
        {
            var expires = request.OrderCreatedAtUtc
                .AddMinutes(minutes);

            if (DateTime.UtcNow >= expires)
                return (null, "PAYMENT_EXPIRED");

            payment = new Payment
            {
                OrderId = request.OrderId,
                CustomerId = request.CustomerId,
                Amount = request.Amount,
                Status = PaymentStatus.Pending,
                PaymentRef = request.OrderId.ToString("N")
                    + Guid.NewGuid().ToString("N")[..12],
                CreatedAtUtc = request.OrderCreatedAtUtc,
                ExpiresAtUtc = expires
            };

            db.Payments.Add(payment);

            await db.SaveChangesAsync(ct);
        }
        else
        {
            if (payment.PaymentMethod != "VNPAY" ||
                payment.CustomerId != request.CustomerId ||
                payment.Amount != request.Amount ||
                payment.CreatedAtUtc != request.OrderCreatedAtUtc)
            {
                return (null, "PAYMENT_DATA_CONFLICT");
            }

            if (payment.Status != PaymentStatus.Pending)
                return (null, "PAYMENT_NOT_PAYABLE");

            if (vnpay.IsPaymentExpired(payment))
                return (null, "PAYMENT_EXPIRED");
        }

        if (vnpay.IsPaymentExpired(payment))
            return (null, "PAYMENT_EXPIRED");

        var url = vnpay.CreatePaymentUrl(
            payment,
            request.ClientIp);

        await transaction.CommitAsync(ct);

        return (new PaymentSessionResponse(
            payment.Id,
            payment.OrderId,
            payment.PaymentRef,
            payment.Status,
            payment.ExpiresAtUtc,
            url), null);
    }
}
