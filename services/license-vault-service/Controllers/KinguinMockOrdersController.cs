using System.Text.Json;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Kinguin;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/supplier/kinguin/mock-orders")]
[Authorize(Policy = "LicenseCoordinatorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class KinguinMockOrdersController(
    KinguinCatalog supplier, ICatalogClient catalog, LicenseDbContext db,
    ILicenseEncryption encryption) : ControllerBase
{
    public sealed record MockRequest(Guid RequestId, Guid ProductId, int Quantity);
    private string? Owner => User.FindFirst("sub")?.Value;

    [HttpPost]
    public Task<IActionResult> Create(MockRequest request, CancellationToken ct) => CreateCore(request, false, ct);

    [HttpPost("async")]
    public Task<IActionResult> Enqueue(MockRequest request, CancellationToken ct) => CreateCore(request, true, ct);

    private async Task<IActionResult> CreateCore(MockRequest request, bool queued, CancellationToken ct)
    {
        if (supplier.Mode != "Mock") return NotFound();
        var owner = Owner;
        if (string.IsNullOrWhiteSpace(owner) || owner.Length > 255) return Forbid();
        if (request.RequestId == Guid.Empty || request.ProductId == Guid.Empty || request.Quantity is < 1 or > 10)
            return BadRequest(new { error = "INVALID_MOCK_ORDER" });

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockAsync($"kinguin-mock-request:{request.RequestId:N}", ct);
        var prior = await db.KinguinMockOrders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == request.RequestId, ct);
        if (prior is not null)
        {
            if (prior.OwnerId != owner) return NotFound();
            if (prior.ProductId != request.ProductId || prior.Quantity != request.Quantity)
                return Conflict(new { error = "MOCK_REQUEST_CONFLICT" });
            await transaction.CommitAsync(ct);
            return prior.Status == "MOCK_PENDING" ? StatusCode(202, Result(prior, true)) : Ok(Result(prior, true));
        }

        string? mapping;
        KinguinProduct? quote;
        try
        {
            mapping = supplier.Mapping(request.ProductId);
            if (string.IsNullOrWhiteSpace(mapping)) return NotFound(new { error = "KINGUIN_MAPPING_NOT_FOUND" });
            if (!await catalog.ProductExistsAsync(request.ProductId, ct)) return NotFound(new { error = "PRODUCT_NOT_FOUND" });
            quote = await supplier.GetAsync(mapping, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or JsonException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(503, new { error = "KINGUIN_UNAVAILABLE" });
        }
        if (quote is null) return NotFound(new { error = "MOCK_PRODUCT_NOT_FOUND" });

        if (queued)
        {
            var pending = new KinguinMockOrder
            {
                Id = request.RequestId, OwnerId = owner, ProductId = request.ProductId,
                SupplierProductId = mapping, Quantity = request.Quantity, TotalCost = 0,
                EncryptedKeys = "[]", CreatedAtUtc = DateTime.UtcNow,
                Status = "MOCK_PENDING", NextPublishAtUtc = DateTime.UtcNow
            };
            db.KinguinMockOrders.Add(pending);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return StatusCode(202, Result(pending, false));
        }

        // All local mappings of the same supplier SKU share one simulated stock pool.
        await LockAsync($"kinguin-mock-stock:{mapping}", ct);
        var used = await db.KinguinMockOrders.Where(x => x.SupplierProductId == mapping && x.Status == "MOCK_FULFILLED")
            .SumAsync(x => (long)x.Quantity, ct);
        if (used + request.Quantity > quote.TextQty)
            return Conflict(new { error = "MOCK_OUT_OF_STOCK" });

        var keys = Enumerable.Range(1, request.Quantity)
            .Select(i => encryption.Encrypt($"DEMO-NOT-REDEEMABLE-{request.RequestId:N}-{i}"))
            .ToArray();
        var order = new KinguinMockOrder
        {
            Id = request.RequestId, OwnerId = owner, ProductId = request.ProductId,
            SupplierProductId = mapping, Quantity = request.Quantity,
            TotalCost = quote.Price * request.Quantity,
            EncryptedKeys = JsonSerializer.Serialize(keys), CreatedAtUtc = DateTime.UtcNow
        };
        db.KinguinMockOrders.Add(order);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return Ok(Result(order, false));
    }

    [HttpGet("{requestId:guid}")]
    public async Task<IActionResult> Get(Guid requestId, CancellationToken ct)
    {
        if (supplier.Mode != "Mock") return NotFound();
        var owner = Owner;
        if (string.IsNullOrWhiteSpace(owner)) return Forbid();
        var order = await db.KinguinMockOrders.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == requestId && x.OwnerId == owner, ct);
        return order is null ? NotFound() : Ok(Result(order, true));
    }

    [HttpGet("{requestId:guid}/demo-keys")]
    public async Task<IActionResult> DemoKeys(Guid requestId, CancellationToken ct)
    {
        if (supplier.Mode != "Mock") return NotFound();
        var owner = Owner;
        if (string.IsNullOrWhiteSpace(owner)) return Forbid();
        var order = await db.KinguinMockOrders.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == requestId && x.OwnerId == owner && x.Status == "MOCK_FULFILLED", ct);
        if (order is null) return NotFound();
        var keys = JsonSerializer.Deserialize<string[]>(order.EncryptedKeys) ?? [];
        return Ok(new { requestId, simulated = true, keys = keys.Select(encryption.Decrypt).ToArray() });
    }

    private Task LockAsync(string key, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({key}, 0))", ct);

    private static object Result(KinguinMockOrder order, bool replayed) => new
    {
        requestId = order.Id, order.ProductId, order.SupplierProductId, order.Quantity,
        order.TotalCost, currency = "EUR", status = order.Status, replayed,
        simulated = true, charged = false, order.CreatedAtUtc
    };
}
