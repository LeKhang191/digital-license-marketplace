using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Catalog.Api.Application.DTOs;
using Catalog.Api.Application.Interfaces;
using Catalog.Api.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Authorize(Policy = "AdminOnly")]
[Route("api/steam-import")]
public sealed class SteamImportController(SteamMetadataProvider steam, IProductService products, ILogger<SteamImportController> log) : ControllerBase
{
    public sealed record ImportRequest([Required, StringLength(500)] string App,
        [Range(typeof(decimal), "5000", "9999999999")] decimal Price);

    [HttpGet("preview")]
    public Task<IActionResult> Preview([FromQuery] string app, CancellationToken ct) => Import(app, null, ct);

    [HttpPost]
    public Task<IActionResult> Create(ImportRequest request, CancellationToken ct) => Import(request.App, request.Price, ct);

    [HttpPost("refresh-requirements")]
    public async Task<IActionResult> RefreshRequirements(CancellationToken ct)
    {
        var updated = await products.RefreshMissingRequirementsAsync(ct);
        return Ok(new { updated });
    }

    private async Task<IActionResult> Import(string app, decimal? price, CancellationToken ct)
    {
        if (!SteamMetadataProvider.TryAppId(app, out var id)) return BadRequest(new { error = "Steam App ID or store URL is invalid." });
        if (price.HasValue && decimal.Truncate(price.Value) != price.Value) return BadRequest(new { error = "Price must be whole VND." });
        SteamMetadata? metadata;
        try { metadata = await steam.FetchAsync(id, ct); }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            log.LogWarning("Metadata request failed: {Type}, HTTP {Status}", ex.GetType().Name, (ex as HttpRequestException)?.StatusCode);
            return StatusCode(503, new { error = "Game metadata unavailable. Check provider credentials and network." });
        }
        if (metadata is null) return NotFound(new { error = "Game not found for this Steam App ID." });
        if (!price.HasValue) return Ok(metadata);
        var (product, conflict) = await products.CreateAsync(new ProductUpsertRequest
        {
            Name = metadata.Name, Slug = $"steam-{id}", Category = "Game", Description = metadata.Description,
            Price = price.Value, IsActive = true, ImportedMetadata = metadata
        }, ct);
        return conflict ? Conflict(new { error = "This Steam game already exists." }) : StatusCode(201, product);
    }
}
