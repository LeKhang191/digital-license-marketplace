
using LicenseVault.Api.Application.DTOs;
using LicenseVault.Api.Domain.Entities;

namespace LicenseVault.Api.Application.Interfaces;

public interface ILicenseService
{
    Task<ImportLicensesResponse> ImportAsync(
        Guid productId,
        IReadOnlyList<string> keys,
        CancellationToken ct,
        LicenseImportBatch batch
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
