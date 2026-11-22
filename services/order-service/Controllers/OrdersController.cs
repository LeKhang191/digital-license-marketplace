using System.Net;
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
                UserId, User.FindFirst("email")?.Value,
                request, ClientIp(), ct);

            if (error is not null) return MapError(error);

            return CreatedAtAction(
                nameof(Get), new { id = order!.Id }, order);
        }
        catch (HttpRequestException)
        {
            return StatusCode(503,
                new { error = "Catalog service unavailable" });
        }
    }

    [HttpPost("{id:guid}/pay")]
    public async Task<IActionResult> Pay(Guid id, CancellationToken ct)
    {
        if (UserId is null) return Unauthorized();

        var (order, error) = await orders.PayAsync(
            UserId, id, ClientIp(), ct);

        return error is not null ? MapError(error) : Ok(order);
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

    private IActionResult MapError(string error) => error switch
    {
        OrderErrors.PaymentNotConfigured => StatusCode(503,
            new { error = "VNPay is not configured" }),
        OrderErrors.OrderNotFound => NotFound(),
        _ => BadRequest(new { error })
    };

    // VNPay cần IPv4 của khách; ưu tiên X-Forwarded-For nếu có
    private string ClientIp()
    {
        var raw = Request.Headers["X-Forwarded-For"]
            .FirstOrDefault()?.Split(',')[0].Trim();

        if (!IPAddress.TryParse(raw, out var ip))
            ip = HttpContext.Connection.RemoteIpAddress;

        if (ip is null || IPAddress.IsLoopback(ip)) return "127.0.0.1";

        return ip.IsIPv4MappedToIPv6
            ? ip.MapToIPv4().ToString()
            : ip.ToString();
    }
}
