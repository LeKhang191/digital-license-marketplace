namespace OrderService.Domain.Entities;

public sealed class RefundOperation
{
    public Guid OrderId { get; set; }

    public Guid RequestId { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = "";

    public string PaymentRef { get; set; } = "";

    public string Mode { get; set; } = "";

    public string Reason { get; set; } = "";

    public string RequestedBy { get; set; } = "";

    public Guid? DefectiveLicenseId { get; set; }

    public string Status { get; set; } = "REQUESTED";

    public Guid? RefundId { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime NextRetryAtUtc { get; set; } =
        DateTime.UtcNow;

    public int RetryCount { get; set; }

    public string? LastError { get; set; }
}
