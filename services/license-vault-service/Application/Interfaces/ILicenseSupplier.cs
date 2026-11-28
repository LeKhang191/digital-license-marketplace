namespace LicenseVault.Api.Application.Interfaces;

// Batch 1: read-only capability. Purchase/fulfillment contracts are separate future work.
public sealed record SupplierQuote(string ProductId, int KinguinId, decimal Price,
    int Qty, int TextQty, int TotalQty);

public interface ILicenseSupplier
{
    string Provider { get; }
    string Mode { get; }
    Task<string?> MappingAsync(Guid productId, CancellationToken ct);
    Task<SupplierQuote?> GetAsync(string supplierProductId, CancellationToken ct);
}
