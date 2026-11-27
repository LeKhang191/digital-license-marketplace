namespace LicenseVault.Api.Domain.Entities;

public sealed class KinguinMockOrder
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string SupplierProductId { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public int Quantity { get; set; }
    public decimal TotalCost { get; set; }
    public string EncryptedKeys { get; set; } = "";
    public string Status { get; set; } = "MOCK_FULFILLED";
    public DateTime? NextPublishAtUtc { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
