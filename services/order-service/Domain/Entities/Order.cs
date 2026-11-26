namespace OrderService.Domain.Entities;

public sealed class Order
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PaymentMethod { get; set; } = "VNPAY";

    public string KeycloakUserId { get; set; } = "";

    public string? CustomerEmail { get; set; }

    public decimal TotalAmount { get; set; }

    public string Status { get; set; } = OrderStatus.Pending;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    // VNPay
    public string? PaymentRef { get; set; }

    public string? VnpTransactionNo { get; set; }

    public string? VnpPayDate { get; set; }

    public DateTime? PaidAtUtc { get; set; }

    public DateTime? PaidEventSentAtUtc { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}
