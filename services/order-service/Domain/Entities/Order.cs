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

    // ---- Thanh toán VNPay ----
    // vnp_TxnRef của lần thanh toán hiện tại: 32 ký tự đầu = Id đơn (dạng N)
    public string? PaymentRef { get; set; }
    public string? VnpTransactionNo { get; set; }
    public string? VnpPayDate { get; set; }
    public DateTime? PaidAtUtc { get; set; }

    // Đã phát OrderPaidEvent lên RabbitMQ chưa (để IPN gọi lại thì phát bù)
    public DateTime? PaidEventSentAtUtc { get; set; }

    public List<OrderItem> Items { get; set; } = new();
}
