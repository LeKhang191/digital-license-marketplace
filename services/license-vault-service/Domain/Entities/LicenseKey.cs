
namespace LicenseVault.Api.Domain.Entities;

public sealed class LicenseKey
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid ProductId { get; set; }

    public Guid? ImportBatchId { get; set; }

    public string EncryptedKey { get; set; } = string.Empty;

    public string KeyFingerprint { get; set; } = string.Empty;

    public string Status { get; set; } = LicenseStatus.Available;

    public Guid? OrderId { get; set; }

    public Guid? OrderItemId { get; set; }

    public string? CustomerId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? AssignedAtUtc { get; set; }

    public DateTime? RevealedAtUtc { get; set; }
}
