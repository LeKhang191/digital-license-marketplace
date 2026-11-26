namespace LicenseVault.Api.Domain.Entities;

public sealed class LicenseRefundGuard
{
    public Guid OrderId { get; set; }

    public Guid RequestId { get; set; }

    public string CustomerId { get; set; } = "";

    public string Mode { get; set; } = "";

    public string Status { get; set; } = "PREPARED";

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime? FinalizedAtUtc { get; set; }
}
