using System.Net.Http.Json;

namespace OrderService.Infrastructure.License;

public sealed record LicenseAvailability(
    Guid ProductId,
    int Available,
    bool InStock);

public sealed class LicenseClient(HttpClient http)
{
    public async Task<int?> GetAvailableAsync(
        Guid productId,
        CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(
                $"api/licenses/availability/{productId}", ct);

            response.EnsureSuccessStatusCode();

            var data = await response.Content
                .ReadFromJsonAsync<LicenseAvailability>(ct);

            if (data is null ||
                data.ProductId != productId ||
                data.Available < 0 ||
                data.InStock != (data.Available > 0))
                return null;

            return data.Available;
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            ex is System.Text.Json.JsonException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
