
using LicenseVault.Api.Application.DTOs;

namespace LicenseVault.Api.Application.Interfaces;

public interface ILicenseService
{
    Task<ImportLicensesResponse> ImportAsync(
        Guid productId,
        IReadOnlyList<string> keys,
        CancellationToken ct
    );

    Task<IReadOnlyList<LicenseInventoryResponse>>
        GetInventoryAsync(CancellationToken ct);

    Task<IReadOnlyList<LicenseKeyResponse>>
        GetProductLicensesAsync(
            Guid productId,
            CancellationToken ct
        );

    Task<bool> DisableAsync(
        Guid id,
        CancellationToken ct
    );
}
