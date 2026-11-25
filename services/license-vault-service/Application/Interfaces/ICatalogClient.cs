
namespace LicenseVault.Api.Application.Interfaces;

public interface ICatalogClient
{
    Task<bool> ProductExistsAsync(
        Guid productId,
        CancellationToken ct
    );
}

