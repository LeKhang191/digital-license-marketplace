using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public static class OrderErrors
{
    public const string ProductNotFound = "Product not found";
    public const string OrderNotFound = "Order not found";
    public const string NotPayable = "Order is not payable";
    public const string PaymentReconciliationRequired = "Payment link cannot be reissued until the original transaction is reconciled. Check your order or contact support.";
    public const string PaymentNotConfigured = "PAYMENT_NOT_CONFIGURED";
}

public interface IOrderManager
{
    Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email, CreateOrderRequest request,
        string clientIp, CancellationToken ct);

    Task<(OrderResponse? Order, string? Error)> PayAsync(
        string userId, Guid id, string clientIp, CancellationToken ct);

    Task<IReadOnlyList<OrderResponse>> ListAsync(
        string userId, CancellationToken ct);

    Task<OrderResponse?> GetAsync(
        string userId, Guid id, CancellationToken ct);
}

public sealed class OrderManager(
    OrderDbContext db,
    ICatalogClient catalog,
    VnPayService vnpay
) : IOrderManager
{
    private static string NewPaymentRef(Guid orderId) =>
        orderId.ToString("N") + DateTime.UtcNow.ToString("yyMMddHHmmss");

    public async Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email, CreateOrderRequest request,
        string clientIp, CancellationToken ct)
    {
        if (!vnpay.IsConfigured)
            return (null, OrderErrors.PaymentNotConfigured);

        var product = await catalog.GetProductAsync(request.ProductId, ct);

        if (product is null || !product.IsActive)
            return (null, OrderErrors.ProductNotFound);

        var order = new Order
        {
            KeycloakUserId = userId,
            CustomerEmail = email,
            Status = OrderStatus.Pending,
            TotalAmount = product.Price * request.Quantity
        };
        order.PaymentRef = NewPaymentRef(order.Id);

        for (var i = 0; i < request.Quantity; i++)
        {
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Price = product.Price
            });
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        var url = vnpay.CreatePaymentUrl(order, clientIp);
        return (OrderResponse.From(order) with { PaymentUrl = url }, null);
    }

    public async Task<(OrderResponse? Order, string? Error)> PayAsync(
        string userId, Guid id, string clientIp, CancellationToken ct)
    {
        if (!vnpay.IsConfigured)
            return (null, OrderErrors.PaymentNotConfigured);

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == id && o.KeycloakUserId == userId, ct);

        if (order is null) return (null, OrderErrors.OrderNotFound);
        if (order.Status != OrderStatus.Pending)
            return (null, OrderErrors.NotPayable);

        // Never overwrite a reference that may still receive a successful IPN.
        // Reissuing payment requires a separate, reconciled payment-attempt model.
        return (null, OrderErrors.PaymentReconciliationRequired);
    }

    public async Task<IReadOnlyList<OrderResponse>> ListAsync(
        string userId, CancellationToken ct)
    {
        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.KeycloakUserId == userId)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(ct);

        return orders.Select(OrderResponse.From).ToList();
    }

    public async Task<OrderResponse?> GetAsync(
        string userId, Guid id, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == id && o.KeycloakUserId == userId, ct);

        return order is null ? null : OrderResponse.From(order);
    }
}
