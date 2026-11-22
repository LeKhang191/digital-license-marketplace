using Microsoft.AspNetCore.Authorization;
using Catalog.Api.Application.DTOs;
using Catalog.Api.Application.Interfaces;
using Catalog.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Catalog.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController(
    IProductService products
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? category = null,
        CancellationToken ct = default)
    {
        if (page < 1 || pageSize < 1 || pageSize > 100)
            return BadRequest("Invalid pagination");

        return Ok(await products.ListAsync(
            page, pageSize, search, category, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        CancellationToken ct)
    {
        var product = await products.GetAsync(id, ct);

        return product is null
            ? NotFound()
            : Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Create(
        [FromBody] ProductUpsertRequest request,
        CancellationToken ct)
    {
        var result = await products.CreateAsync(request, ct);

        if (result.conflict)
            return Conflict("Slug already exists");

        return CreatedAtAction(
            nameof(Get),
            new { id = result.product!.Id },
            result.product
        );
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Update(
        Guid id,
        [FromBody] ProductUpsertRequest request,
        CancellationToken ct)
    {
        var result = await products.UpdateAsync(
            id, request, ct);

        if (result.conflict)
            return Conflict("Slug already exists");

        return result.product is null
            ? NotFound()
            : Ok(result.product);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Delete(
        Guid id,
        CancellationToken ct)
    {
        return await products.DeleteAsync(id, ct)
            ? NoContent()
            : NotFound();
    }
}