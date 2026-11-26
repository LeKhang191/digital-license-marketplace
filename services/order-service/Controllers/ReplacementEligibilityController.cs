using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Authorize(Roles = "license_coordinator")]
[Route("api/internal/replacement-eligibility")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReplacementEligibilityController(
    OrderDbContext db
) : ControllerBase
{
    [HttpGet("{ticketId:guid}")]
    public async Task<IActionResult> Get(
        Guid ticketId,
        CancellationToken ct)
    {
        var staffId = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(staffId))
            return Unauthorized();

        var ticket = await db.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == ticketId &&
                x.AssignedRole == "license_coordinator" &&
                x.HandledBy == staffId, ct);

        if (ticket is null)
            return NotFound();

        if (ticket.Status != "IN_PROGRESS" ||
            ticket.Category is not
                ("INVALID_KEY" or "KEY_ALREADY_USED") ||
            ticket.LicenseId is null)
        {
            return Conflict(new
            {
                code = "TICKET_NOT_ELIGIBLE"
            });
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.Id == ticket.OrderId &&
                x.KeycloakUserId == ticket.CustomerId, ct);

        if (order is null)
            return NotFound();

        if (order.Status != OrderStatus.Completed)
            return Conflict(new
            {
                code = "ORDER_NOT_ELIGIBLE"
            });

        return Ok(new
        {
            ticketId = ticket.Id,
            ticket.OrderId,
            ticket.LicenseId,
            ticket.CustomerId,
            ticket.HandledBy,
            items = order.Items.Select(x => new
            {
                x.Id,
                x.ProductId
            })
        });
    }
}
