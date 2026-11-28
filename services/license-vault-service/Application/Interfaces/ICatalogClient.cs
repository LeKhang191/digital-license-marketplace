
namespace LicenseVault.Api.Application.Interfaces;

public interface ICatalogClient
{
    Task<string?> ProductSlugAsync(Guid productId, CancellationToken ct);

    Task<bool> ProductExistsAsync(
        Guid productId,
        CancellationToken ct
    );
}

