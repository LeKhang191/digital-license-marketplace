
using LicenseVault.Api.Application.DTOs;
using LicenseVault.Api.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
public sealed class LicensesController(
    ILicenseService licenses,
    ICatalogClient catalog,
    LicenseDbContext db
) : ControllerBase
{
    [HttpPost("import")]
    public async Task<IActionResult> Import(
        [FromBody] ImportLicensesRequest request,
        CancellationToken ct)
    {
        return await ImportKeys(
            request.ProductId,
            request.Keys,
            ct, request.SupplierId, request.SupplierName, request.Reference, request.UnitCostVnd, "MANUAL");
    }

    [HttpPost("import-file")]
    [RequestSizeLimit(1_200_000)]
    public async Task<IActionResult> ImportFile(
        [FromForm] Guid productId,
        [FromForm] IFormFile file,
        CancellationToken ct,
        [FromForm] Guid? supplierId = null,
        [FromForm] string? supplierName = null,
        [FromForm] string? reference = null,
        [FromForm] decimal? unitCostVnd = null)
    {
        if (file.Length == 0 ||
            file.Length > 1_048_576)
        {
            return BadRequest(new
            {
                error = "File must be between 1 byte and 1 MB."
            });
        }

        var extension = Path.GetExtension(file.FileName)
            .ToLowerInvariant();

        if (extension != ".csv" && extension != ".txt")
        {
            return BadRequest(new
            {
                error = "Only CSV and TXT files are supported."
            });
        }

        var keys = new List<string>();

        await using var stream = file.OpenReadStream();

        try
        {
            if (extension == ".csv")
            {
                using var parser = new TextFieldParser(stream);

                parser.TextFieldType = FieldType.Delimited;
                parser.SetDelimiters(",");
                parser.HasFieldsEnclosedInQuotes = true;

                bool first = true;

                while (!parser.EndOfData)
                {
                    ct.ThrowIfCancellationRequested();

                    var fields = parser.ReadFields();

                    if (fields is null)
                        continue;

                    if (fields.Length != 1)
                    {
                        return BadRequest(new
                        {
                            error = "CSV must contain one column."
                        });
                    }

                    var key = fields[0].Trim();

                    if (first)
                    {
                        first = false;

                        if (key.Equals(
                            "license_key",
                            StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }
                    }

                    keys.Add(key);

                    if (keys.Count > 1000)
                        return BadRequest("Maximum 1000 keys.");
                }
            }
            else
            {
                using var reader = new StreamReader(stream);

                while (await reader.ReadLineAsync(ct)
                    is { } line)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    keys.Add(line.Trim());

                    if (keys.Count > 1000)
                        return BadRequest("Maximum 1000 keys.");
                }
            }
        }
        catch (MalformedLineException)
        {
            return BadRequest(new
            {
                error = "Invalid CSV format."
            });
        }

        return await ImportKeys(productId, keys, ct, supplierId, supplierName, reference, unitCostVnd, extension == ".csv" ? "CSV" : "TXT");
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> Inventory(
        CancellationToken ct)
    {
        return Ok(
            await licenses.GetInventoryAsync(ct));
    }

    [HttpGet("products/{id:guid}")]
    public async Task<IActionResult> GetProductLicenses(
        Guid id,
        CancellationToken ct)
    {
        return Ok(
            await licenses.GetProductLicensesAsync(id, ct));
    }

    [HttpPatch("{id:guid}/disable")]
    public async Task<IActionResult> Disable(
        Guid id,
        CancellationToken ct)
    {
        var disabled =
            await licenses.DisableAsync(id, ct);

        if (!disabled)
        {
            return Conflict(new
            {
                error = "License does not exist or is not available."
            });
        }

        return NoContent();
    }

    [HttpGet("import-batches")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ImportBatches(CancellationToken ct) => Ok(await db.Set<LicenseImportBatch>()
        .AsNoTracking().OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id).Take(200).ToListAsync(ct));

    [HttpGet("replacement-history/{productId:guid}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> ReplacementHistory(Guid productId, CancellationToken ct) => Ok(
        await db.LicenseReplacements.AsNoTracking().Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.CreatedAtUtc).ThenBy(x => x.TicketId).Take(200)
            .Select(x => new { x.TicketId, x.OldLicenseId, x.NewLicenseId, x.CreatedAtUtc }).ToListAsync(ct));

    private async Task<IActionResult> ImportKeys(
        Guid productId,
        IReadOnlyList<string> keys,
        CancellationToken ct, Guid? supplierId, string? supplierName, string? reference, decimal? unitCostVnd, string method)
    {
        if (productId == Guid.Empty)
        {
            return BadRequest(new
            {
                error = "Invalid product ID."
            });
        }

        if (keys.Count == 0 || keys.Count > 1000)
        {
            return BadRequest(new
            {
                error = "Import between 1 and 1000 keys."
            });
        }

        try
        {
            if (!await catalog.ProductExistsAsync(
                productId, ct))
            {
                return NotFound(new
                {
                    error = "Active product not found."
                });
            }
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return StatusCode(503, new
            {
                error = "Catalog service unavailable."
            });
        }

        supplierName = supplierName?.Trim();
        reference = reference?.Trim();
        if (supplierName?.Length > 160 || reference?.Length > 120 ||
            supplierName?.Any(char.IsControl) == true || reference?.Any(char.IsControl) == true ||
            unitCostVnd is < 0 or > 9999999999999999m ||
            (unitCostVnd.HasValue && decimal.Round(unitCostVnd.Value, 2) != unitCostVnd.Value) ||
            (!supplierId.HasValue && string.IsNullOrWhiteSpace(supplierName) && (!string.IsNullOrWhiteSpace(reference) || unitCostVnd.HasValue)))
            return BadRequest(new { error = "Invalid supplier metadata." });
        if (!supplierId.HasValue && !string.IsNullOrWhiteSpace(supplierName))
        {
            var normalized = supplierName.ToLowerInvariant();
            supplierId = await db.Set<Supplier>().Where(x => x.NormalizedName == normalized).Select(x => (Guid?)x.Id).SingleOrDefaultAsync(ct);
            if (!supplierId.HasValue) return BadRequest(new { error = "Chọn nhà cung cấp đã đăng ký." });
        }
        var owner = User.FindFirst("sub")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 255) return Forbid();
        var batch = new LicenseImportBatch { Id = Guid.NewGuid(), ProductId = productId,
            SupplierId = supplierId, SupplierName = "Unspecified (legacy import)",
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference, UnitCostVnd = unitCostVnd,
            ImportedBy = owner, CreatedAtUtc = DateTime.UtcNow, Method = method };
        try { return Ok(await licenses.ImportAsync(productId, keys, ct, batch)); }
        catch (SupplierUnavailableException) { return Conflict(new { error = "Nhà cung cấp không tồn tại hoặc đã ngừng hoạt động." }); }
    }
}
