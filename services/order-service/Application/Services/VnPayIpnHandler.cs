using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public sealed record IpnResult(string RspCode, string Message);
public sealed class VnPayIpnHandler(OrderDbContext db, VnPayService vnpay)
{
    public async Task<IpnResult> HandleAsync(
        IReadOnlyDictionary<string, string> query, CancellationToken ct)
    {
        string Get(string k) => query.TryGetValue(k, out var v) ? v : "";
        if (!vnpay.IsConfigured) return new("99", "VNPay not configured");
        if (!vnpay.ValidateSignature(query)) return new("97", "Invalid signature");
        var txnRef = Get("vnp_TxnRef");
        if (txnRef.Length < 32 || !Guid.TryParseExact(txnRef[..32], "N", out var orderId))
            return new("01", "Order not found");
        var order = await db.Orders.AsNoTracking().FirstOrDefaultAsync(o => o.Id == orderId, ct);
        if (order is null || order.PaymentRef != txnRef) return new("01", "Order not found");
        if (!long.TryParse(Get("vnp_Amount"), out var amount) ||
            amount != VnPayService.ToVnpAmount(order.TotalAmount)) return new("04", "Invalid amount");
        if (order.Status != OrderStatus.Pending) return new("02", "Order already confirmed");
        // A failed notification is not proof that no money can arrive later.
        // Keep the order pending until authoritative reconciliation is implemented.
        if (Get("vnp_ResponseCode") != "00" || Get("vnp_TransactionStatus") != "00")
            return new("00", "Confirm Success");
        var txNo = Get("vnp_TransactionNo");
        var payDate = Get("vnp_PayDate");
        if (string.IsNullOrWhiteSpace(txNo) || txNo.Length > 32 || payDate.Length != 14 || !payDate.All(char.IsAsciiDigit))
            return new("99", "Invalid transaction details");
        DateTime? paidAt = DateTime.UtcNow;
        var rows = await db.Orders
            .Where(o => o.Id == orderId && o.PaymentRef == txnRef && o.Status == OrderStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Paid)
                .SetProperty(o => o.VnpTransactionNo, txNo)
                .SetProperty(o => o.VnpPayDate, payDate)
                .SetProperty(o => o.PaidAtUtc, paidAt), ct);
        // The durable PAID row is the dispatch marker; the worker retries independently of IPN.
        return rows == 1 ? new("00", "Confirm Success") : new("02", "Order already confirmed");
    }
}
