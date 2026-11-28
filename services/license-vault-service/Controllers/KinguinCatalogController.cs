using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Infrastructure.Kinguin;
using Microsoft.AspNetCore.Authorization;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/supplier/kinguin")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KinguinCatalogController(ILicenseSupplier supplier, ICatalogClient catalog, LicenseDbContext db) : ControllerBase
{
    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> Get(Guid productId, CancellationToken ct, [FromQuery] int quantity = 1)
    {
        if (supplier.Mode != "Mock" && supplier.Mode != "Sandbox")
            return StatusCode(503, new { error = "KINGUIN_DISABLED" });
        if (quantity is < 1 or > 10)
            return BadRequest(new { error = "INVALID_QUANTITY" });
        try
        {
            var mapping = await supplier.MappingAsync(productId, ct);
            if (string.IsNullOrWhiteSpace(mapping))
                return NotFound(new { error = "KINGUIN_MAPPING_NOT_FOUND" });
            if (!await catalog.ProductExistsAsync(productId, ct))
                return NotFound(new { error = "PRODUCT_NOT_FOUND" });
            var quote = await supplier.GetAsync(mapping, ct);
            if (supplier.Mode == "Mock" && quote is not null)
            {
                var used = await db.KinguinMockOrders.Where(x => x.SupplierProductId == mapping && x.Status == "MOCK_FULFILLED")
                    .SumAsync(x => (long)x.Quantity, ct);
                quote = quote with { TextQty = (int)Math.Max(0L, quote.TextQty - used),
                    Qty = (int)Math.Max(0L, quote.Qty - used), TotalQty = (int)Math.Max(0L, quote.TotalQty - used) };
            }
            return Ok(new { productId, supplierProductId = mapping, mode = supplier.Mode,
                currency = "EUR", inStock = quote?.TextQty > 0, quote,
                quantity, canFulfill = quote is not null && quote.TextQty >= quantity,
                estimatedCost = quote is not null && quote.TextQty >= quantity
                    ? (decimal?)(quote.Price * quantity) : null,
                previewOnly = true, demoAutoImport = supplier is KinguinCatalog k && k.DemoAutoImport });
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
            or InvalidDataException or InvalidOperationException or System.Security.Cryptography.CryptographicException or FormatException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(503, new { error = "KINGUIN_UNAVAILABLE" });
        }
    }
}
