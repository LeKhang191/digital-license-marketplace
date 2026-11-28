namespace LicenseVault.Api.Domain.Entities;

public sealed class LicenseImportBatch
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public string SupplierName { get; set; } = "";
    public string? Reference { get; set; }
    public decimal? UnitCostVnd { get; set; }
    public string Method { get; set; } = "";
    public string ImportedBy { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; }
    public int Total { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int Invalid { get; set; }
}
