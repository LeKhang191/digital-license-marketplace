namespace PaymentService.Domain.Entities;

public sealed class Refund
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid PaymentId { get; set; }

    public Guid RequestId { get; set; }

    public decimal Amount { get; set; }

    public string Reason { get; set; } = "";

    public string RequestedBy { get; set; } = "";

    public string Status { get; set; } =
        RefundStatus.Pending;

    public string? ProviderRefundRef { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? CompletedAtUtc { get; set; }
}
