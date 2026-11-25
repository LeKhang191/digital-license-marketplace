
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

        if (order.Status != OrderStatus.Pending)
        {
            return new(
                "02",
                "Order already confirmed");
        }

        var success =
            Get("vnp_ResponseCode") == "00" &&
            Get("vnp_TransactionStatus") == "00";

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

            var txNo = Get("vnp_TransactionNo");
            var payDate = Get("vnp_PayDate");

            var paidAt = DateTime.UtcNow;

            var updated = await db.Orders
                .Where(o =>
                    o.Id == orderId &&
                    o.PaymentRef == txnRef &&
                    o.Status == OrderStatus.Pending)
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
