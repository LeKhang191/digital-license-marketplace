using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

public interface IOrderManager
{
    Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email,
        CreateOrderRequest request, CancellationToken ct);

    Task<IReadOnlyList<OrderResponse>> ListAsync(
        string userId, CancellationToken ct);

    Task<OrderResponse?> GetAsync(
        string userId, Guid id, CancellationToken ct);
}

public sealed class OrderManager(
    OrderDbContext db,
    ICatalogClient catalog
) : IOrderManager
{
    public async Task<(OrderResponse? Order, string? Error)> CreateAsync(
        string userId, string? email,
        CreateOrderRequest request, CancellationToken ct)
    {
        var product = await catalog.GetProductAsync(request.ProductId, ct);

        if (product is null || !product.IsActive)
            return (null, "Product not found");

        var order = new Order
        {
            KeycloakUserId = userId,
            CustomerEmail = email,
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

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        return (OrderResponse.From(order), null);
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
