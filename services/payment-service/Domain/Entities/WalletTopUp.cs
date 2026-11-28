namespace PaymentService.Domain.Entities;

public sealed class WalletTopUp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = "";

    public decimal Amount { get; set; }

    public string PaymentRef { get; set; } = "";

    public string Status { get; set; } = PaymentStatus.Pending;

    public string? VnpTransactionNo { get; set; }

    public string? VnpPayDate { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; }

    public DateTime? PaidAtUtc { get; set; }
}
