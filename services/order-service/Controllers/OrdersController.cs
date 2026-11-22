using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.DTOs;
using OrderService.Application.Services;

namespace OrderService.Controllers;

[ApiController]
[Route("api/orders")]
[Authorize]
public sealed class OrdersController(
    IOrderManager orders
) : ControllerBase
{
    private string? UserId => User.FindFirst("sub")?.Value;

    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken ct)
    {
        if (UserId is null) return Unauthorized();

        try
        {
            var (order, error) = await orders.CreateAsync(
                UserId, User.FindFirst("email")?.Value, request, ct);

            if (error is not null)
                return BadRequest(new { error });

            return CreatedAtAction(
                nameof(Get), new { id = order!.Id }, order);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503,
                new { error = "Catalog service unavailable" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (UserId is null) return Unauthorized();
        return Ok(await orders.ListAsync(UserId, ct));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (UserId is null) return Unauthorized();

        var order = await orders.GetAsync(UserId, id, ct);
        return order is null ? NotFound() : Ok(order);
    }
}
