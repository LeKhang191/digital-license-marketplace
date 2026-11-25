
using System.Net;
using LicenseVault.Api.Application.Interfaces;

namespace LicenseVault.Api.Infrastructure.Catalog;

public sealed class CatalogClient(
    HttpClient http
) : ICatalogClient
{
    public async Task<bool> ProductExistsAsync(
        Guid productId,
        CancellationToken ct)
    {
        using var response = await http.GetAsync(
            $"api/products/{productId}",
            ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();

        return true;
    }
}

