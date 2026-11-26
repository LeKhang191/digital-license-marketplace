using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.License;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Payment;

namespace OrderService.Application.Services;

public static class OrderErrors
{
    public const string ProductNotFound = "Product not found";
    public const string OrderNotFound = "Order not found";
    public const string NotPayable = "Order is not payable";
    public const string PaymentExpired = "PAYMENT_EXPIRED";
    public const string InsufficientStock = "INSUFFICIENT_STOCK";
    public const string InventoryUnavailable = "INVENTORY_UNAVAILABLE";
    public const string PaymentNotConfigured = "PAYMENT_NOT_CONFIGURED";
    public const string PaymentUnavailable = "PAYMENT_UNAVAILABLE";
    public const string InsufficientWalletBalance =
        "INSUFFICIENT_WALLET_BALANCE";
    public const string InvalidPaymentMethod =
        "INVALID_PAYMENT_METHOD";
}

public interface IOrderManager
{
    Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email,
        bool emailVerified, CreateOrderRequest request,
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
    PaymentClient payments
) : IOrderManager
{
    private async Task<(OrderResponse? Order, string? Error)>
        CreatePaymentSessionAsync(
            Order order,
            string clientIp,
            CancellationToken ct)
    {
        var request = new PaymentSessionRequest(
            order.Id,
            order.KeycloakUserId,
            order.TotalAmount,
            order.CreatedAtUtc,
            clientIp);

        var (session, error) =
            await payments.CreateSessionAsync(request, ct);

        if (session is null)
        {
            return (null, error switch
            {
                "PAYMENT_EXPIRED" =>
                    OrderErrors.PaymentExpired,

                "PAYMENT_NOT_CONFIGURED" =>
                    OrderErrors.PaymentNotConfigured,

                "PAYMENT_NOT_PAYABLE" =>
                    OrderErrors.NotPayable,

                _ => OrderErrors.PaymentUnavailable
            });
        }

        if (session.OrderId != order.Id ||
            session.PaymentId == Guid.Empty ||
            session.Status != "PENDING" ||
            session.ExpiresAtUtc <= DateTime.UtcNow ||
            string.IsNullOrWhiteSpace(session.PaymentRef) ||
            session.PaymentRef.Length > 64 ||
            !Uri.TryCreate(
                session.PaymentUrl,
                UriKind.Absolute,
                out var url) ||
            url.Scheme != Uri.UriSchemeHttps)
        {
            return (null, OrderErrors.PaymentUnavailable);
        }

        var updated = await db.Orders
            .Where(o =>
                o.Id == order.Id &&
                o.Status == OrderStatus.Pending &&
                (o.PaymentRef == null ||
                 o.PaymentRef == session.PaymentRef))
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    o => o.PaymentRef,
                    session.PaymentRef),
                ct);

        if (updated != 1)
            return (null, OrderErrors.NotPayable);

        order.PaymentRef = session.PaymentRef;

        return (
            OrderResponse.From(order) with
            {
                PaymentUrl = session.PaymentUrl
            },
            null
        );
    }

    public async Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email,
        bool emailVerified, CreateOrderRequest request,
        string clientIp, CancellationToken ct)
    {
        var method = request.PaymentMethod?
            .Trim().ToUpperInvariant();

        if (method is not ("VNPAY" or "WALLET"))
            return (null, OrderErrors.InvalidPaymentMethod);

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
            CustomerEmailVerified = emailVerified
                && !string.IsNullOrWhiteSpace(email),
            PaymentMethod = method,
            Status = OrderStatus.Pending,
            TotalAmount = product.Price * request.Quantity
        };

        for (var i = 0; i < request.Quantity; i++)
        {
            order.Items.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                Price = product.Price
            });
        }

        // Persist the Wallet reference before Payment can publish.
        if (order.PaymentMethod == "WALLET")
            order.PaymentRef = $"W{order.Id:N}";

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // Creation alone must not debit the Wallet.
        if (order.PaymentMethod == "WALLET")
            return (OrderResponse.From(order), null);

        return await CreatePaymentSessionAsync(
            order, clientIp, ct);
    }

    public async Task<(OrderResponse? Order, string? Error)> PayAsync(
        string userId, Guid id, string clientIp, CancellationToken ct)
    {
        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == id && o.KeycloakUserId == userId, ct);

        if (order is null) return (null, OrderErrors.OrderNotFound);
        if (order.PaymentMethod == "WALLET" &&
            order.Status is OrderStatus.Paid or OrderStatus.Completed)
        {
            return (await GetAsync(userId, id, ct), null);
        }

        if (order.Status != OrderStatus.Pending)
            return (null, OrderErrors.NotPayable);

        if (order.Items.Count == 0)
            return (null, OrderErrors.NotPayable);

        // Recover a committed payment before checking
        // mutable catalog and inventory state.
        if (order.PaymentMethod == "WALLET")
        {
            var lookup = new PaymentSessionRequest(
                order.Id,
                order.KeycloakUserId,
                order.TotalAmount,
                order.CreatedAtUtc,
                clientIp);

            var (existingPayment, lookupError) =
                await payments.FindWalletPaymentAsync(
                    lookup, ct);

            if (existingPayment is not null)
                return (await GetAsync(userId, id, ct), null);

            if (lookupError != "PAYMENT_NOT_FOUND")
                return (null, OrderErrors.PaymentUnavailable);
        }

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

        if (order.PaymentMethod == "WALLET")
        {
            var request = new PaymentSessionRequest(
                order.Id,
                order.KeycloakUserId,
                order.TotalAmount,
                order.CreatedAtUtc,
                clientIp);

            var (payment, error) =
                await payments.PayWithWalletAsync(request, ct);

            if (payment is null)
            {
                return (null, error switch
                {
                    "INSUFFICIENT_WALLET_BALANCE" =>
                        OrderErrors.InsufficientWalletBalance,
                    "PAYMENT_EXPIRED" =>
                        OrderErrors.PaymentExpired,
                    "PAYMENT_NOT_PAYABLE" =>
                        OrderErrors.NotPayable,
                    _ => OrderErrors.PaymentUnavailable
                });
            }

            // Order status is finalized by the payment event.
            return (await GetAsync(userId, id, ct), null);
        }

        return await CreatePaymentSessionAsync(
            order, clientIp, ct);
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

        var orderIds = orders.Select(o => o.Id).ToArray();

        var results = await db.FulfillmentResults
            .AsNoTracking()
            .Where(x => orderIds.Contains(x.OrderId))
            .ToDictionaryAsync(
                x => x.OrderId,
                x => x.Status,
                ct);

        return orders.Select(o => OrderResponse.From(
            o,
            results.GetValueOrDefault(o.Id)))
            .ToList();
    }

    public async Task<OrderResponse?> GetAsync(
        string userId, Guid id, CancellationToken ct)
    {
        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == id && o.KeycloakUserId == userId, ct);

        if (order is null)
            return null;

        var fulfillmentStatus = await db.FulfillmentResults
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .Select(x => x.Status)
            .FirstOrDefaultAsync(ct);

        return OrderResponse.From(order, fulfillmentStatus);
    }
}
