using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public sealed record IpnResult(string RspCode, string Message);
public sealed class VnPayIpnHandler(
    OrderDbContext db,
    VnPayService vnpay,
    IEventPublisher publisher,
    ILogger<VnPayIpnHandler> log)
{
    public async Task<IpnResult> HandleAsync(
        IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        string Get(string k) => query.TryGetValue(k, out var v) ? v : "";

        if (!vnpay.IsConfigured) return new("99", "VNPay not configured");
        if (!vnpay.ValidateSignature(query)) return new("97", "Invalid signature");

        var txnRef = Get("vnp_TxnRef");

        if (txnRef.Length < 32 ||
            !Guid.TryParseExact(txnRef[..32], "N", out var orderId))
            return new("01", "Order not found");

        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId, ct);

        if (order is null || order.PaymentRef != txnRef)
            return new("01", "Order not found");

        if (!long.TryParse(Get("vnp_Amount"), out var amount) ||
            amount != VnPayService.ToVnpAmount(order.TotalAmount))
            return new("04", "Invalid amount");

        var success = Get("vnp_ResponseCode") == "00" &&
                      Get("vnp_TransactionStatus") == "00";

        DateTime? paidAt;

        if (order.Status == OrderStatus.Pending)
        {
            if (!success)
            {
                await db.Orders
                    .Where(o => o.Id == orderId &&
                                o.Status == OrderStatus.Pending)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(o => o.Status, OrderStatus.Canceled), ct);

                return new("00", "Confirm Success");
            }

            var txNo = Get("vnp_TransactionNo");
            var payDate = Get("vnp_PayDate");
            paidAt = DateTime.UtcNow;
            var rows = await db.Orders
                .Where(o => o.Id == orderId &&
                            o.Status == OrderStatus.Pending)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.Status, OrderStatus.Paid)
                    .SetProperty(o => o.VnpTransactionNo, txNo)
                    .SetProperty(o => o.VnpPayDate, payDate)
                    .SetProperty(o => o.PaidAtUtc, paidAt), ct);

            if (rows == 0) return new("02", "Order already confirmed");
        }
        else if (order.Status == OrderStatus.Paid &&
                 order.PaidEventSentAtUtc is null)
        {
            paidAt = order.PaidAtUtc ?? DateTime.UtcNow;
        }
        else
        {
            return new("02", "Order already confirmed");
        }

        try
        {
            var evt = new OrderPaidEvent(
                order.Id,
                order.KeycloakUserId,
                order.CustomerEmail,
                order.TotalAmount,
                order.Items
                    .Select(i => new OrderItemRef(i.Id, i.ProductId))
                    .ToList(),
                paidAt!.Value);

            await publisher.PublishAsync(EventRoutes.OrderPaid, evt, ct);

            DateTime? sentAt = DateTime.UtcNow;
            await db.Orders
                .Where(o => o.Id == orderId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(o => o.PaidEventSentAtUtc, sentAt), ct);

            return new("00", "Confirm Success");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Publish OrderPaidEvent failed for {OrderId}",
                orderId);
            return new("99", "Unknown error");
        }
    }
}
