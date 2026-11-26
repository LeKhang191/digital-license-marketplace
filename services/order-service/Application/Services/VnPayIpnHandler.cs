
using System.Globalization;
using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public sealed record IpnResult(
    string RspCode,
    string Message);

public sealed class VnPayIpnHandler(
    OrderDbContext db,
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
                ? value
                : "";

        if (!vnpay.IsConfigured)
            return new("99", "VNPay not configured");

        if (!vnpay.ValidateSignature(query))
            return new("97", "Invalid signature");

        if (!vnpay.ValidateMerchant(Get("vnp_TmnCode")))
            return new("99", "Invalid merchant");

        var txnRef = Get("vnp_TxnRef");

        if (txnRef.Length < 32 ||
            !Guid.TryParseExact(
                txnRef[..32],
                "N",
                out var orderId))
        {
            return new("01", "Order not found");
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId,
                ct);

        if (order is null ||
            order.PaymentRef != txnRef)
        {
            return new("01", "Order not found");
        }

        if (!long.TryParse(
                Get("vnp_Amount"),
                out var amount) ||
            amount != VnPayService.ToVnpAmount(
                order.TotalAmount))
        {
            return new("04", "Invalid amount");
        }

        var success =
            Get("vnp_ResponseCode") == "00" &&
            Get("vnp_TransactionStatus") == "00";

        var recoverable =
            success &&
            order.Status == OrderStatus.Canceled &&
            order.PaidAtUtc == null &&
            string.IsNullOrEmpty(order.VnpTransactionNo);

        if (order.Status != OrderStatus.Pending && !recoverable)
        {
            if (success &&
                !string.IsNullOrEmpty(order.VnpTransactionNo) &&
                order.VnpTransactionNo != Get("vnp_TransactionNo"))
            {
                log.LogWarning(
                    "Conflicting payment transaction for {OrderId}; reconciliation required",
                    orderId);

                return new("99", "Payment reconciliation required");
            }

            return new("02", "Order already confirmed");
        }

        var txNo = Get("vnp_TransactionNo");
        var payDate = Get("vnp_PayDate");

        if (success)
        {
            if (txNo.Length == 0 ||
                txNo.Length > 32 ||
                txNo.Any(c => c < '0' || c > '9') ||
                txNo.All(c => c == '0'))
            {
                return new("99", "Invalid transaction number");
            }

            if (!DateTime.TryParseExact(
                    payDate,
                    "yyyyMMddHHmmss",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out _))
            {
                return new("99", "Invalid payment date");
            }
        }

        try
        {
            await using var transaction =
                await db.Database
                    .BeginTransactionAsync(ct);

            if (!success)
            {
                var canceled = await db.Orders
                    .Where(o =>
                        o.Id == orderId &&
                        o.PaymentRef == txnRef &&
                        o.Status == OrderStatus.Pending)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(
                            o => o.Status,
                            OrderStatus.Canceled),
                        ct);

                if (canceled == 0)
                {
                    return new(
                        "02",
                        "Order already confirmed");
                }

                await transaction.CommitAsync(ct);

                return new("00", "Confirm Success");
            }

            var paidAt = DateTime.UtcNow;

            var updated = await db.Orders
                .Where(o =>
                    o.Id == orderId &&
                    o.PaymentRef == txnRef &&
                    o.PaidAtUtc == null &&
                    (o.VnpTransactionNo == null ||
                     o.VnpTransactionNo == "") &&
                    (o.Status == OrderStatus.Pending ||
                     o.Status == OrderStatus.Canceled))
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(
                            o => o.Status,
                            OrderStatus.Paid)
                        .SetProperty(
                            o => o.VnpTransactionNo,
                            txNo)
                        .SetProperty(
                            o => o.VnpPayDate,
                            payDate)
                        .SetProperty(
                            o => o.PaidAtUtc,
                            (DateTime?)paidAt),
                    ct);

            if (updated == 0)
            {
                return new(
                    "02",
                    "Order already confirmed");
            }

            var evt = new OrderPaidEvent(
                order.Id,
                order.KeycloakUserId,
                order.CustomerEmail,
                order.TotalAmount,
                order.Items
                    .Select(i => new OrderItemRef(
                        i.Id,
                        i.ProductId))
                    .ToList(),
                paidAt);

            db.OutboxMessages.Add(
                new OutboxMessage
                {
                    AggregateId = order.Id,

                    RoutingKey = EventRoutes.OrderPaid,

                    Payload = JsonSerializer.Serialize(
                        evt,
                        Json),

                    CreatedAtUtc = paidAt,

                    NextRetryAtUtc = paidAt
                });

            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            log.LogInformation(
                "Order {OrderId} paid and event stored in outbox",
                order.Id);

            return new("00", "Confirm Success");
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            log.LogError(
                ex,
                "Payment transaction failed for {OrderId}",
                orderId);

            return new("99", "Unknown error");
        }
    }
}
