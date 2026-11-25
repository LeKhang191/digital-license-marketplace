
using LicenseVault.Api.Application.DTOs;
using LicenseVault.Api.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualBasic.FileIO;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
public sealed class LicensesController(
    ILicenseService licenses,
    ICatalogClient catalog
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
            ct);
    }

    [HttpPost("import-file")]
    [RequestSizeLimit(1_200_000)]
    public async Task<IActionResult> ImportFile(
        [FromForm] Guid productId,
        [FromForm] IFormFile file,
        CancellationToken ct)
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

        return await ImportKeys(productId, keys, ct);
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

    private async Task<IActionResult> ImportKeys(
        Guid productId,
        IReadOnlyList<string> keys,
        CancellationToken ct)
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

        var result = await licenses.ImportAsync(
            productId,
            keys,
            ct);

        return Ok(result);
    }
}
