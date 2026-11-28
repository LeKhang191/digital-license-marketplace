namespace LicenseVault.Api.Domain.Entities;

// One supported integration account per provider. No arbitrary endpoint URLs.
public sealed class SupplierApiConfiguration
{
    public string Provider { get; set; } = "";
    public string EncryptedApiKey { get; set; } = "";
    public DateTime UpdatedAtUtc { get; set; }
}

public sealed class SupplierProductMapping
{
    public string Provider { get; set; } = "";
    public string Mode { get; set; } = "";
    public Guid ProductId { get; set; }
    public string SupplierProductId { get; set; } = "";
}
