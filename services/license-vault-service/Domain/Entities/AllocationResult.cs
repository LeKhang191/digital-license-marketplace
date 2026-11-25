namespace LicenseVault.Api.Domain.Entities;

public sealed class AllocationResult
{
    public Guid OrderId { get; set; }

    public string CustomerId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public string RequestFingerprint { get; set; } = string.Empty;

    public string RoutingKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime NextRetryAtUtc { get; set; } = DateTime.UtcNow;

    public string? LastError { get; set; }
}
