using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Route("api/transactions")]
[Authorize(Policy = "TransactionAuditorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class TransactionAuditController(OrderDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken ct = default)
    {
        if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100)
            return BadRequest(new { error = "Invalid pagination" });

        // Order-side payment records, including orders subsequently refunded.
        var query = db.Orders.AsNoTracking().Where(o => o.PaidAtUtc != null);
        var total = await query.CountAsync(ct);
        var refunded = await query.CountAsync(o => o.Status == OrderStatus.Refunded, ct);
        var items = await query.OrderByDescending(o => o.PaidAtUtc).ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(o => new { o.Id, o.KeycloakUserId, o.CustomerEmail, o.TotalAmount,
                o.PaymentMethod, o.PaymentRef, o.Status, o.PaidAtUtc })
            .ToListAsync(ct);
        return Ok(new { total, refunded, page, pageSize, items });
    }
}
