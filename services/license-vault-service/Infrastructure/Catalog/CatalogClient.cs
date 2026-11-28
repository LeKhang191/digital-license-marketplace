
using System.Net;
using System.Net.Http.Json;
using LicenseVault.Api.Application.Interfaces;

namespace LicenseVault.Api.Infrastructure.Catalog;

public sealed class CatalogClient(
    HttpClient http
) : ICatalogClient
{
    private sealed record ProductIdentity(string Slug);

    public async Task<string?> ProductSlugAsync(Guid productId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/products/{productId}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductIdentity>(cancellationToken: ct))?.Slug;
    }

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

