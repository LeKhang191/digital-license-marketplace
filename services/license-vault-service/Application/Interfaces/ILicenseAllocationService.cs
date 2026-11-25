using Contracts.Events;

namespace LicenseVault.Api.Application.Interfaces;

public interface ILicenseAllocationService
{
    Task ProcessAsync(
        OrderPaidEvent message,
        CancellationToken ct
    );
}
