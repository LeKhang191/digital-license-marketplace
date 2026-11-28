using System.Net;
using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Services;

namespace OrderService.Controllers;

[ApiController]
[Route("api/partner/v1/orders")]
[Authorize(Policy = KeycloakAuthentication.PartnerPolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerOrdersController(
    PartnerOrderService partnerOrders
) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Create(
        [FromBody] CreatePartnerOrderRequest request,
        CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "IDEMPOTENCY_KEY_REQUIRED",
                error = "Idempotency-Key header is required."
            });
        }

        try
        {
            var (order, error) = await partnerOrders.CreateAndPayAsync(
                partnerId,
                idempotencyKey,
                request,
                ClientIp(),
                ct);

            if (error is not null)
                return MapError(error);

            return order!.AlreadyProcessed
                ? Ok(order)
                : CreatedAtAction(nameof(Get), new { id = order.OrderId }, order);
        }
        catch (Exception ex) when (
            ex is HttpRequestException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(503, new
            {
                code = "UPSTREAM_UNAVAILABLE",
                error = "Catalog or payment service is temporarily unavailable."
            });
        }
    }

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION",
                error = "Invalid page or page size."
            });
        }

        var (items, total) = await partnerOrders.ListAsync(partnerId, page, pageSize, ct);
        return Ok(new { partnerId, page, pageSize, total, items });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var order = await partnerOrders.GetAsync(partnerId, id, ct);
        return order is null ? NotFound() : Ok(order);
    }

    private IActionResult MapError(string error) => error switch
    {
        "IDEMPOTENCY_CONFLICT" => Conflict(new
        {
            code = error,
            error = "Idempotency-Key was already used with different order parameters."
        }),
        OrderErrors.OrderNotFound => NotFound(),
        OrderErrors.ProductNotFound => NotFound(new
        {
            code = "PRODUCT_NOT_FOUND",
            error = "Product not found or inactive."
        }),
        OrderErrors.InsufficientWalletBalance => Conflict(new
        {
            code = OrderErrors.InsufficientWalletBalance,
            error = "Partner prepaid wallet balance is insufficient."
        }),
        OrderErrors.InsufficientStock => Conflict(new
        {
            code = OrderErrors.InsufficientStock,
            error = "Insufficient license stock."
        }),
        OrderErrors.PaymentExpired => Conflict(new
        {
            code = OrderErrors.PaymentExpired,
            error = "Payment window expired. Create a new order with a new Idempotency-Key."
        }),
        OrderErrors.NotPayable => Conflict(new
        {
            code = "ORDER_NOT_PAYABLE",
            error = "Order is not payable."
        }),
        OrderErrors.InventoryUnavailable or OrderErrors.PaymentUnavailable => StatusCode(503, new
        {
            code = error,
            error = "Required internal service is temporarily unavailable."
        }),
        _ => BadRequest(new { code = error, error = "Invalid partner order request." })
    };

    private string ClientIp()
    {
        var raw = Request.Headers["X-Forwarded-For"]
            .FirstOrDefault()?.Split(',')[0].Trim();

        if (!IPAddress.TryParse(raw, out var ip))
            ip = HttpContext.Connection.RemoteIpAddress;

        if (ip is null || IPAddress.IsLoopback(ip))
            return "127.0.0.1";

        return ip.IsIPv4MappedToIPv6
            ? ip.MapToIPv4().ToString()
            : ip.ToString();
    }
}
