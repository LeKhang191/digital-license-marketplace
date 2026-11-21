using Catalog.Api.Application.DTOs;

namespace Catalog.Api.Application.Interfaces;

public interface IProductService
{
    Task<PagedProducts> ListAsync(
        int page,
        int pageSize,
        string? search,
        string? category,
        CancellationToken ct
    );

    Task<ProductResponse?> GetAsync(
        Guid id,
        CancellationToken ct
    );

    Task<(ProductResponse? product, bool conflict)> CreateAsync(
        ProductUpsertRequest request,
        CancellationToken ct
    );

    Task<(ProductResponse? product, bool conflict)> UpdateAsync(
        Guid id,
        ProductUpsertRequest request,
        CancellationToken ct
    );

    Task<bool> DeleteAsync(
        Guid id,
        CancellationToken ct
    );
}