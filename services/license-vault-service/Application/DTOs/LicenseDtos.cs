
using System.ComponentModel.DataAnnotations;

namespace LicenseVault.Api.Application.DTOs;

public sealed class ImportLicensesRequest
{
    public Guid ProductId { get; set; }
    public Guid? SupplierId { get; set; }
    public string? SupplierName { get; set; }
    public string? Reference { get; set; }
    public decimal? UnitCostVnd { get; set; }

    [Required]
    [MinLength(1)]
    [MaxLength(1000)]
    public List<string> Keys { get; set; } = [];
}

public sealed record ImportLicensesResponse(
    Guid ProductId,
    int Total,
    int Imported,
    int Duplicates,
    int Invalid,
    Guid? BatchId = null
);

public sealed record LicenseInventoryResponse(
    Guid ProductId,
    int Total,
    int Available,
    int Assigned,
    int Disabled
);

public sealed record LicenseKeyResponse(
    Guid Id,
    Guid ProductId,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? AssignedAtUtc
);
