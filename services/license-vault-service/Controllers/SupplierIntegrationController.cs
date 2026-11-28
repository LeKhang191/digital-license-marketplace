using System.ComponentModel.DataAnnotations;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Kinguin;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/integrations")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupplierIntegrationController(KinguinCatalog supplier, ICatalogClient catalog,
    LicenseDbContext db, ILicenseEncryption encryption) : ControllerBase
{
    public sealed record CredentialInput([Required, MaxLength(2048)] string ApiKey);
    public sealed record MappingInput(Guid ProductId, [Required, MaxLength(128)] string SupplierProductId);
    private bool Enabled => supplier.Mode is "Mock" or "Sandbox";
    private static bool ValidId(string id) => id.Length is > 0 and <= 128 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct) => Ok(new {
        supplier.Provider, supplier.Mode, credentialSource = await supplier.CredentialSourceAsync(ct),
        purchaseEnabled = supplier.Mode == "Mock", demoAutoImport = supplier.DemoAutoImport, productionEnabled = false
    });

    [HttpPost("credentials")]
    public async Task<IActionResult> Credentials(CredentialInput input, CancellationToken ct)
    {
        if (supplier.Mode != "Sandbox") return BadRequest(new { error = "Chỉ cấu hình khóa trong chế độ Sandbox." });
        var key = input.ApiKey.Trim();
        if (key.Length == 0 || key.Any(c => char.IsWhiteSpace(c) || char.IsControl(c))) return BadRequest(new { error = "API Key không hợp lệ." });
        var encrypted = encryption.Encrypt("supplier-api:kinguin:sandbox:" + key);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO supplier_api_configurations ("Provider", "EncryptedApiKey", "UpdatedAtUtc")
            VALUES ({supplier.Provider}, {encrypted}, {DateTime.UtcNow})
            ON CONFLICT ("Provider") DO UPDATE SET "EncryptedApiKey" = EXCLUDED."EncryptedApiKey", "UpdatedAtUtc" = EXCLUDED."UpdatedAtUtc"
            """, ct);
        return NoContent();
    }

    [HttpPost("credentials/clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    {
        await db.Set<SupplierApiConfiguration>().Where(x => x.Provider == supplier.Provider).ExecuteDeleteAsync(ct);
        return NoContent();
    }

    [HttpGet("mappings")]
    public async Task<IActionResult> Mappings(CancellationToken ct) => Ok(await db.Set<SupplierProductMapping>().AsNoTracking()
        .Where(x => x.Provider == supplier.Provider && x.Mode == supplier.Mode).OrderBy(x => x.ProductId)
        .Select(x => new { x.ProductId, x.SupplierProductId }).ToListAsync(ct));

    [HttpGet("mappings/{productId:guid}")]
    public async Task<IActionResult> Mapping(Guid productId, CancellationToken ct) => Ok(new {
        productId, supplierProductId = await supplier.MappingAsync(productId, ct),
        suggestedMockId = await supplier.SuggestedMockIdAsync(productId, ct)
    });

    [HttpPost("mappings")]
    public async Task<IActionResult> SaveMapping(MappingInput input, CancellationToken ct)
    {
        if (!Enabled || input.ProductId == Guid.Empty || !ValidId(input.SupplierProductId)) return BadRequest(new { error = "Mapping không hợp lệ." });
        if (!await catalog.ProductExistsAsync(input.ProductId, ct)) return NotFound(new { error = "PRODUCT_NOT_FOUND" });
        if (supplier.DemoAutoImport && input.SupplierProductId != await supplier.SuggestedMockIdAsync(input.ProductId, ct))
            return BadRequest(new { error = "Chỉ sử dụng mã Mock đúng game trong chế độ nhập kho demo." });
        if (supplier.Mode == "Mock" && input.SupplierProductId.StartsWith("mock-steam-", StringComparison.Ordinal)
            && input.SupplierProductId != await supplier.SuggestedMockIdAsync(input.ProductId, ct))
            return BadRequest(new { error = "Mã Mock không khớp Steam App ID của sản phẩm." });
        if (supplier.Mode == "Mock" && await supplier.GetAsync(input.SupplierProductId, ct) is null)
            return BadRequest(new { error = "Mã sản phẩm Mock không tồn tại." });
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO supplier_product_mappings ("Provider", "Mode", "ProductId", "SupplierProductId")
            VALUES ({supplier.Provider}, {supplier.Mode}, {input.ProductId}, {input.SupplierProductId})
            ON CONFLICT ("Provider", "Mode", "ProductId") DO UPDATE SET "SupplierProductId" = EXCLUDED."SupplierProductId"
            """, ct);
        return NoContent();
    }

    [HttpGet("check")]
    public async Task<IActionResult> Check([FromQuery] string supplierProductId, CancellationToken ct)
    {
        if (!Enabled || !ValidId(supplierProductId)) return BadRequest(new { error = "Chọn mã sản phẩm hợp lệ để kiểm tra." });
        try
        {
            var quote = await supplier.GetAsync(supplierProductId, ct);
            return Ok(new { connected = quote is not null, supplier.Mode, simulated = supplier.Mode == "Mock",
                message = quote is null ? "Không tìm thấy sản phẩm." : "Tra cứu sản phẩm thành công; chưa xác nhận quyền mua." });
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        { return StatusCode(503, new { error = "Không thể kiểm tra kết nối nhà cung cấp." }); }
    }

    [HttpGet("orders")]
    public async Task<IActionResult> Orders(CancellationToken ct)
    {
        var owner = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(owner)) return Forbid();
        return Ok(await db.KinguinMockOrders.AsNoTracking().Where(x => x.OwnerId == owner)
            .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(200)
            .Select(x => new { requestId = x.Id, x.ProductId, x.SupplierProductId, x.Quantity, x.TotalCost, Status = x.Status == "MOCK_IMPORT_PENDING" ? "MOCK_PENDING" : x.Status, x.CreatedAtUtc, imported = db.Set<LicenseImportBatch>().Where(b => b.Id == x.Id && b.Method == "MOCK_API").Select(b => b.Imported).FirstOrDefault(), simulated = true, charged = false }).ToListAsync(ct));
    }
}
