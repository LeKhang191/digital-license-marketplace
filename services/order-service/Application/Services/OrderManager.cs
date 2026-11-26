using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.License;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public static class OrderErrors
{
    public const string ProductNotFound = "Product not found";
    public const string OrderNotFound = "Order not found";
    public const string NotPayable = "Order is not payable";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InventoryUnavailable = "INVENTORY_UNAVAILABLE";
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
    LicenseClient licenses,
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

        var available = await licenses.GetAvailableAsync(
            product.Id, ct);

        if (available is null)
            return (null, OrderErrors.InventoryUnavailable);

        if (available.Value < request.Quantity)
            return (null, OrderErrors.InsufficientStock);

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

        if (order.Items.Count == 0)
            return (null, OrderErrors.NotPayable);

        foreach (var group in order.Items.GroupBy(i => i.ProductId))
        {
            var product = await catalog.GetProductAsync(
                group.Key, ct);

            if (product is null || !product.IsActive)
                return (null, OrderErrors.ProductNotFound);

            var available = await licenses.GetAvailableAsync(
                group.Key, ct);

            if (available is null)
                return (null, OrderErrors.InventoryUnavailable);

            if (available.Value < group.Count())
                return (null, OrderErrors.InsufficientStock);
        }

        order.PaymentRef = NewPaymentRef(order.Id);
        await db.SaveChangesAsync(ct);

        var url = vnpay.CreatePaymentUrl(order, clientIp);
        return (OrderResponse.From(order) with { PaymentUrl = url }, null);
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
