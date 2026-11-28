using System.ComponentModel.DataAnnotations;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/suppliers")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SuppliersController(LicenseDbContext db) : ControllerBase
{
    public sealed record SupplierInput([Required, MaxLength(160)] string Name,
        [MaxLength(500)] string? Website, [EmailAddress, MaxLength(255)] string? Email,
        [MaxLength(80)] string? Phone, [MaxLength(2000)] string? Notes, bool IsActive = true);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rows = await db.Set<Supplier>().AsNoTracking().OrderBy(x => x.Name).Select(x => new {
            x.Id, x.Name, x.Website, x.Email, x.Phone, x.Notes, x.IsActive, x.CreatedAtUtc, x.UpdatedAtUtc,
            BatchCount = db.Set<LicenseImportBatch>().Count(b => b.SupplierId == x.Id),
            Imported = db.Set<LicenseImportBatch>().Where(b => b.SupplierId == x.Id).Sum(b => (long)b.Imported),
            Duplicates = db.Set<LicenseImportBatch>().Where(b => b.SupplierId == x.Id).Sum(b => (long)b.Duplicates),
            KnownCostVnd = db.Set<LicenseImportBatch>().Where(b => b.SupplierId == x.Id).Sum(b => (b.UnitCostVnd ?? 0m) * b.Imported),
            UnknownCostKeys = db.Set<LicenseImportBatch>().Where(b => b.SupplierId == x.Id && b.UnitCostVnd == null).Sum(b => (long)b.Imported)
        }).ToListAsync(ct);
        return Ok(rows);
    }

    [HttpGet("{id:guid}/batches")]
    public async Task<IActionResult> Batches(Guid id, CancellationToken ct)
    {
        if (!await db.Set<Supplier>().AnyAsync(x => x.Id == id, ct)) return NotFound();
        return Ok(await db.Set<LicenseImportBatch>().AsNoTracking().Where(b => b.SupplierId == id)
            .OrderByDescending(b => b.CreatedAtUtc).ThenByDescending(b => b.Id).Take(200).ToListAsync(ct));
    }

    [HttpPost]
    public Task<IActionResult> Create(SupplierInput input, CancellationToken ct) => Save(null, input, ct);
    [HttpPatch("{id:guid}")]
    public Task<IActionResult> Update(Guid id, SupplierInput input, CancellationToken ct) => Save(id, input, ct);

    private async Task<IActionResult> Save(Guid? id, SupplierInput input, CancellationToken ct)
    {
        var name = input.Name.Trim();
        if (name.Length == 0 || name.Any(char.IsControl) || name.Equals("Unspecified (legacy import)", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Tên nguồn hàng không hợp lệ." });
        var website = Clean(input.Website);
        if (website != null && (!Uri.TryCreate(website, UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http") || string.IsNullOrEmpty(url.Host)))
            return BadRequest(new { error = "Website phải là URL HTTP hoặc HTTPS." });
        var normalized = name.ToLowerInvariant();
        if (await db.Set<Supplier>().AnyAsync(x => x.NormalizedName == normalized && x.Id != id, ct))
            return Conflict(new { error = "Nhà cung cấp đã tồn tại." });
        var entity = id.HasValue ? await db.Set<Supplier>().FindAsync(new object[] { id.Value }, ct) : new Supplier { Id = Guid.NewGuid(), CreatedAtUtc = DateTime.UtcNow };
        if (entity == null) return NotFound();
        entity.Name = name; entity.NormalizedName = normalized; entity.Website = website;
        entity.Email = Clean(input.Email); entity.Phone = Clean(input.Phone); entity.Notes = Clean(input.Notes);
        entity.IsActive = input.IsActive; entity.UpdatedAtUtc = DateTime.UtcNow;
        if (!id.HasValue) db.Add(entity);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException e) when (e.InnerException is PostgresException { SqlState: "23505" })
        { return Conflict(new { error = "Nhà cung cấp đã tồn tại." }); }
        return Ok(new { entity.Id });
    }
    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
