using System.ComponentModel.DataAnnotations;
using Catalog.Api.Domain.Entities;

namespace Catalog.Api.Application.DTOs;

public sealed class ProductUpsertRequest
{
    [Required, StringLength(160, MinimumLength = 2)]
    public string Name { get; set; } = "";

    [Required]
    [StringLength(180)]
    [RegularExpression("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    public string Slug { get; set; } = "";

    [StringLength(2000)]
    public string Description { get; set; } = "";

    [Required, StringLength(80, MinimumLength = 2)]
    public string Category { get; set; } = "";

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    [System.Text.Json.Serialization.JsonIgnore]
    public SteamMetadata? ImportedMetadata { get; set; }
}

public sealed record ProductResponse(
    Guid Id,
    string Name,
    string Slug,
    string Description,
    string Category,
    decimal Price,
    bool IsActive,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    SteamMetadata? Steam
)
{
    public static ProductResponse From(Product p) => new(
        p.Id, p.Name, p.Slug, p.Description,
        p.Category, p.Price, p.IsActive,
        p.CreatedAtUtc, p.UpdatedAtUtc,
        p.SteamMetadataJson is null ? null : System.Text.Json.JsonSerializer.Deserialize<SteamMetadata>(p.SteamMetadataJson)
    );
}

public sealed record PagedProducts(
    IReadOnlyList<ProductResponse> Items,
    int Page,
    int PageSize,
    int Total
);