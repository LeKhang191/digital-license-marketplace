using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.VnPay;

namespace OrderService.Application.Services;

public static class OrderErrors
{
    public const string ProductNotFound = "Product not found";
    public const string OrderNotFound = "Order not found";
    public const string NotPayable = "Order is not payable";
    public const string NotRefundable = "Order is not in REFUNDING status";
    public const string InvalidAmount = "Order total must be a whole VND amount of at least 5,000 VND";
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

    Task<IReadOnlyList<OrderResponse>> ListRefundsAsync(CancellationToken ct);

    Task<(OrderResponse? Order, string? Error)> CompleteRefundAsync(Guid id, CancellationToken ct);
}

public sealed class OrderManager(
    OrderDbContext db,
    ICatalogClient catalog,
    VnPayService vnpay,
    IEventPublisher publisher,
    ILogger<OrderManager> log
) : IOrderManager
{
    private const decimal MinVnPayAmountVnd = 5000m;

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

        var totalAmount = product.Price * request.Quantity;
        if (totalAmount < MinVnPayAmountVnd || decimal.Truncate(totalAmount) != totalAmount)
            return (null, OrderErrors.InvalidAmount);

        var order = new Order
        {
            KeycloakUserId = userId,
            CustomerEmail = email,
            Status = OrderStatus.Pending,
            TotalAmount = totalAmount
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

    public async Task<IReadOnlyList<OrderResponse>> ListRefundsAsync(CancellationToken ct)
    {
        var orders = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Refunding || o.Status == OrderStatus.Refunded)
            .OrderByDescending(o => o.CreatedAtUtc)
            .Take(200)
            .ToListAsync(ct);

        return orders.Select(OrderResponse.From).ToList();
    }

    public async Task<(OrderResponse? Order, string? Error)> CompleteRefundAsync(Guid id, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, ct);

        if (order is null) return (null, OrderErrors.OrderNotFound);
        if (order.Status == OrderStatus.Refunded) return (OrderResponse.From(order), null);
        if (order.Status != OrderStatus.Refunding) return (null, OrderErrors.NotRefundable);

        var rows = await db.Orders
            .Where(o => o.Id == id && o.Status == OrderStatus.Refunding)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunded), ct);

        if (rows == 0)
        {
            var latest = await db.Orders.AsNoTracking().Include(o => o.Items).FirstOrDefaultAsync(o => o.Id == id, ct);
            return latest?.Status == OrderStatus.Refunded
                ? (OrderResponse.From(latest), null)
                : (null, OrderErrors.NotRefundable);
        }

        order.Status = OrderStatus.Refunded;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await publisher.PublishAsync(
                EventRoutes.OrderRefunded,
                new OrderRefundedEvent(order.Id, order.KeycloakUserId, order.CustomerEmail, order.TotalAmount),
                timeout.Token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            log.LogWarning(ex, "Order {OrderId} refunded in DB, but failed to publish order.refunded notification", order.Id);
        }

        return (OrderResponse.From(order), null);
    }
}
