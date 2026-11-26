using System.ComponentModel.DataAnnotations;
using OrderService.Domain.Entities;

namespace OrderService.Application.DTOs;

public sealed class CreateOrderRequest
{
    [Required]
    public Guid ProductId { get; set; }

    [Range(1, 10)]
    public int Quantity { get; set; } = 1;
}

public sealed record OrderItemResponse(
    Guid Id,
    Guid ProductId,
    decimal Price);

public sealed record OrderResponse(
    Guid Id,
    string Status,
    decimal TotalAmount,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderItemResponse> Items,
    string? PaymentUrl = null,
    string FulfillmentStatus = "NOT_STARTED")
{
    public static OrderResponse From(
        Order o,
        string? fulfillmentStatus = null) => new(
        o.Id,
        o.Status,
        o.TotalAmount,
        o.CreatedAtUtc,
        o.Items
            .Select(i => new OrderItemResponse(i.Id, i.ProductId, i.Price))
            .ToList(),
        FulfillmentStatus: fulfillmentStatus ?? (o.Status switch
        {
            OrderStatus.Paid => "PROCESSING",
            OrderStatus.Completed => "ASSIGNED",
            OrderStatus.Refunding => "UNKNOWN",
            OrderStatus.Refunded => "UNKNOWN",
            _ => "NOT_STARTED"
        }));
}
