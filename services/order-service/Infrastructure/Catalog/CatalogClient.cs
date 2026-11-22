using System.Net;
using System.Net.Http.Json;

namespace OrderService.Infrastructure.Catalog;

public sealed record CatalogProduct(
    Guid Id,
    string Name,
    decimal Price,
    bool IsActive);

public interface ICatalogClient
{
    Task<CatalogProduct?> GetProductAsync(Guid id, CancellationToken ct);
}

// Gọi đồng bộ sang Catalog Service để lấy giá (không tin giá từ client)
public sealed class CatalogClient(HttpClient http) : ICatalogClient
{
    public async Task<CatalogProduct?> GetProductAsync(
        Guid id, CancellationToken ct)
    {
        using var res = await http.GetAsync($"api/products/{id}", ct);

        if (res.StatusCode == HttpStatusCode.NotFound) return null;

        res.EnsureSuccessStatusCode();
        return await res.Content.ReadFromJsonAsync<CatalogProduct>(ct);
    }
}
