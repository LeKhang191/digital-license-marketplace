using System.Net.Http.Json;
using System.Text.Json;
using Catalog.Api.Application.Interfaces;
using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/partner/v1/catalog/products")]
[Authorize(Policy = KeycloakAuthentication.PartnerPolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerCatalogController(
    IProductService products,
    IHttpClientFactory clients
) : ControllerBase
{
    private sealed record StockItem(
        Guid ProductId,
        int Available,
        bool InStock);

    private sealed record BatchStockResponse(
        List<StockItem> Items);

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { code = "INVALID_PAGINATION", error = "Invalid pagination." });

        var result = await products.ListAsync(
            page, pageSize, search, category, ct, includeInactive: false);

        var ids = result.Items.Select(x => x.Id).ToArray();
        var stock = await FetchBatchStockAsync(ids, ct);

        if (stock is null)
        {
            return StatusCode(503, new
            {
                code = "INVENTORY_UNAVAILABLE",
                error = "Unable to retrieve live license inventory."
            });
        }

        var items = result.Items.Select(p =>
        {
            var available = stock.GetValueOrDefault(p.Id, 0);
            return new
            {
                p.Id,
                p.Name,
                p.Slug,
                p.Category,
                p.Price,
                currency = "VND",
                availableStock = available,
                inStock = available > 0
            };
        }).ToList();

        return Ok(new
        {
            items,
            result.Page,
            result.PageSize,
            result.Total
        });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken ct)
    {
        var product = await products.GetAsync(id, ct);
        if (product is null)
            return NotFound();

        var stock = await FetchSingleStockAsync(id, ct);
        if (stock is null)
        {
            return StatusCode(503, new
            {
                code = "INVENTORY_UNAVAILABLE",
                error = "Unable to retrieve live license inventory."
            });
        }

        return Ok(new
        {
            product.Id,
            product.Name,
            product.Slug,
            product.Description,
            product.Category,
            product.Price,
            currency = "VND",
            availableStock = stock.Available,
            inStock = stock.InStock
        });
    }

    private async Task<Dictionary<Guid, int>?> FetchBatchStockAsync(
        Guid[] productIds,
        CancellationToken ct)
    {
        if (productIds.Length == 0)
            return new Dictionary<Guid, int>();

        try
        {
            var client = clients.CreateClient("CatalogLicenses");
            using var response = await client.PostAsJsonAsync(
                "api/licenses/availability/batch",
                new { productIds },
                ct);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<BatchStockResponse>(
                cancellationToken: ct);

            if (body?.Items is null)
                return null;

            return body.Items.ToDictionary(x => x.ProductId, x => x.Available);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }

    private async Task<StockItem?> FetchSingleStockAsync(
        Guid productId,
        CancellationToken ct)
    {
        try
        {
            var client = clients.CreateClient("CatalogLicenses");
            using var response = await client.GetAsync(
                $"api/licenses/availability/{productId:D}",
                ct);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<StockItem>(
                cancellationToken: ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return null;
        }
    }
}
