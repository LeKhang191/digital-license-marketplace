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
}
