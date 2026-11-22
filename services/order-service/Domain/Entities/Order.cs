namespace OrderService.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // UUID (claim "sub") lấy từ JWT của Keycloak
    public string KeycloakUserId { get; set; } = "";

    // Dùng để Notification Service gửi email chứa Key
    public string? CustomerEmail { get; set; }

    public decimal TotalAmount { get; set; }
    public string Status { get; set; } = OrderStatus.Pending;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<OrderItem> Items { get; set; } = new();
}
