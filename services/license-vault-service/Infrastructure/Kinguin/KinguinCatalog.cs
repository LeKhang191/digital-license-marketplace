using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LicenseVault.Api.Infrastructure.Kinguin;

public sealed record KinguinProduct(
    string ProductId, int KinguinId, decimal Price,
    int Qty, int TextQty, int TotalQty);

public sealed class KinguinCatalog(HttpClient http, IConfiguration config)
{
    public string Mode => config["Kinguin:Mode"] ?? "Disabled";

    public string? Mapping(Guid id)
    {
        var mappings = new Dictionary<Guid, string>();
        void Add(string? local, string? supplier)
        {
            if (!Guid.TryParse(local, out var product) || product == Guid.Empty ||
                string.IsNullOrWhiteSpace(supplier) || supplier.Length > 128 ||
                !supplier.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
                throw new InvalidOperationException("Invalid supplier mapping.");
            if (mappings.TryGetValue(product, out var existing) && existing != supplier)
                throw new InvalidOperationException("Conflicting supplier mapping.");
            mappings[product] = supplier;
        }
        var legacy = config["Kinguin:LocalProductId"];
        if (!string.IsNullOrWhiteSpace(legacy)) Add(legacy, config["Kinguin:ProductId"]);
        foreach (var entry in (config["Kinguin:ProductMappings"] ?? "")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2) throw new InvalidOperationException("Invalid supplier mapping.");
            Add(pair[0], pair[1]);
        }
        return mappings.GetValueOrDefault(id);
    }

    public async Task<KinguinProduct?> GetAsync(string id, CancellationToken ct)
    {
        if (Mode == "Mock")
            return id switch
            {
                "demo-product-001" => new KinguinProduct(id, 10001, 5.25m, 10, 8, 12),
                "demo-product-002" => new KinguinProduct(id, 10002, 8.50m, 4, 3, 5),
                "demo-product-003" => new KinguinProduct(id, 10003, 3.10m, 0, 0, 0),
                _ => null
            };

        if (Mode != "Sandbox")
            throw new InvalidOperationException("Kinguin is disabled.");

        var key = config["Kinguin:ApiKey"];
        if (string.IsNullOrWhiteSpace(key))
            throw new InvalidOperationException("Sandbox API key is missing.");

        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"api/v2/products/{Uri.EscapeDataString(id)}?projection=stock");
        request.Headers.Add("X-Api-Key", key);
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        var product = await response.Content.ReadFromJsonAsync<KinguinWireProduct>(cancellationToken: ct);
        if (product is null || product.ProductId != id || product.KinguinId is null or <= 0 ||
            product.Price is null or < 0 || product.Qty is null or < 0 ||
            product.TextQty is null or < 0 || product.TotalQty is null or < 0 ||
            product.TextQty > product.Qty || product.Qty > product.TotalQty)
            throw new InvalidDataException("Invalid supplier product response.");
        return new KinguinProduct(id, product.KinguinId.Value, product.Price.Value,
            product.Qty.Value, product.TextQty.Value, product.TotalQty.Value);
    }

    private sealed record KinguinWireProduct(
        [property: JsonPropertyName("productId")] string? ProductId,
        [property: JsonPropertyName("kinguinId")] int? KinguinId,
        [property: JsonPropertyName("price")] decimal? Price,
        [property: JsonPropertyName("qty")] int? Qty,
        [property: JsonPropertyName("textQty")] int? TextQty,
        [property: JsonPropertyName("totalQty")] int? TotalQty);
}
