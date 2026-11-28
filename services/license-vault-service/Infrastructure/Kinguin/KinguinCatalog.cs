using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Kinguin;

public sealed class KinguinCatalog(HttpClient http, IConfiguration config,
    LicenseDbContext db, ILicenseEncryption encryption, ICatalogClient catalog) : ILicenseSupplier
{
    public string Provider => "kinguin";
    public async Task<string?> MappingAsync(Guid id, CancellationToken ct)
    {
        var mapping = await db.Set<SupplierProductMapping>().AsNoTracking()
            .Where(x => x.Provider == Provider && x.Mode == Mode && x.ProductId == id)
            .Select(x => x.SupplierProductId).SingleOrDefaultAsync(ct);
        // Demo purchases use the exact game SKU, not an old generic fixture.
        if (DemoAutoImport) return await SuggestedMockIdAsync(id, ct);
        if (mapping is not null) return mapping;
        // Preserve legacy configured mappings for regression fixtures and existing workflows.
        return Mapping(id) ?? await SuggestedMockIdAsync(id, ct);
    }

    public async Task<string?> SuggestedMockIdAsync(Guid id, CancellationToken ct)
    {
        return Mode == "Mock" ? MockGameCatalog.ForSlug(await catalog.ProductSlugAsync(id, ct)) : null;
    }

    public async Task<string> CredentialSourceAsync(CancellationToken ct)
    {
        if (Mode != "Sandbox") return "not-required";
        if (await db.Set<SupplierApiConfiguration>().AnyAsync(x => x.Provider == Provider, ct)) return "stored";
        return string.IsNullOrWhiteSpace(config["Kinguin:ApiKey"]) ? "missing" : "environment";
    }

    public bool DemoAutoImport => Mode == "Mock" && config.GetValue<bool>("Kinguin:DemoAutoImport");

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

    public async Task<SupplierQuote?> GetAsync(string id, CancellationToken ct)
    {
        if (Mode == "Mock")
            return id switch
            {
                "demo-product-001" => new SupplierQuote(id, 10001, 5.25m, 10, 8, 12),
                "demo-product-002" => new SupplierQuote(id, 10002, 8.50m, 4, 3, 5),
                "demo-product-003" => new SupplierQuote(id, 10003, 3.10m, 0, 0, 0),
                _ => MockGameCatalog.Find(id)
            };

        if (Mode != "Sandbox")
            throw new InvalidOperationException("Kinguin is disabled.");

        var stored = await db.Set<SupplierApiConfiguration>().AsNoTracking()
            .SingleOrDefaultAsync(x => x.Provider == Provider, ct);
        var key = stored is null ? config["Kinguin:ApiKey"] : encryption.Decrypt(stored.EncryptedApiKey);
        if (stored is not null)
        {
            const string prefix = "supplier-api:kinguin:sandbox:";
            if (key is null || !key.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Invalid supplier credential.");
            key = key[prefix.Length..];
        }
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
        return new SupplierQuote(id, product.KinguinId.Value, product.Price.Value,
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
