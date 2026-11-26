using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Authorize(Roles = "license_coordinator")]
[Route("api/internal/support/orders")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class InternalSupportOrdersController(
    OrderDbContext db,
    IConfiguration config
) : ControllerBase
{
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id,
        [FromQuery] string customerId,
        CancellationToken ct)
    {
        if (!new InternalServiceAuth(config)
            .Validate(Request))
        {
            return Unauthorized();
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.KeycloakUserId == customerId, ct);

        if (order is null)
            return NotFound();

        return Ok(new
        {
            order.Id,
            order.Status,
            Items = order.Items.Select(x => new
            {
                x.Id,
                x.ProductId
            })
        });
    }
}
