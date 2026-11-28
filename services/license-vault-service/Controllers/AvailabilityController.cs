using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/availability")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AvailabilityController(
    LicenseDbContext db, IConfiguration config
) : ControllerBase
{
    [AllowAnonymous]
    [HttpGet("{productId:guid}")]
    public async Task<IActionResult> Get(
        Guid productId,
        CancellationToken ct)
    {
        if (productId == Guid.Empty)
            return BadRequest(new
            {
                error = "Invalid product ID."
            });

        var allowDemo = config["Kinguin:Mode"] == "Mock" && config.GetValue<bool>("Kinguin:DemoAutoImport");
        var available = await db.LicenseKeys
            .AsNoTracking()
            .CountAsync(k =>
                k.ProductId == productId &&
                k.Status == LicenseStatus.Available && (allowDemo || !db.Set<LicenseImportBatch>().Any(b => b.Id == k.ImportBatchId && b.Method == "MOCK_API")),
                ct);

        return Ok(new
        {
            productId,
            available,
            inStock = available > 0
        });
    }

    public sealed record BatchAvailabilityRequest(
        IReadOnlyList<Guid> ProductIds);

    [AllowAnonymous]
    [HttpPost("batch")]
    public async Task<IActionResult> Batch(
        [FromBody] BatchAvailabilityRequest request,
        CancellationToken ct)
    {
        if (request.ProductIds is null || request.ProductIds.Count > 100)
        {
            return BadRequest(new
            {
                error = "ProductIds must contain at most 100 entries."
            });
        }

        var ids = request.ProductIds
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
            return Ok(new { items = Array.Empty<object>() });

        var allowDemo = config["Kinguin:Mode"] == "Mock" &&
            config.GetValue<bool>("Kinguin:DemoAutoImport");

        var counts = await db.LicenseKeys
            .AsNoTracking()
            .Where(k =>
                ids.Contains(k.ProductId) &&
                k.Status == LicenseStatus.Available &&
                (allowDemo || !db.Set<LicenseImportBatch>().Any(
                    b => b.Id == k.ImportBatchId && b.Method == "MOCK_API")))
            .GroupBy(k => k.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.ProductId, x => x.Count, ct);

        var items = ids.Select(id =>
        {
            var available = counts.GetValueOrDefault(id, 0);
            return new
            {
                productId = id,
                available,
                inStock = available > 0
            };
        }).ToList();

        return Ok(new { items });
    }
}
