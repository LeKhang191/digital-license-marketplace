using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportService.Domain.Entities;
using SupportService.Infrastructure.Persistence;

namespace SupportService.Controllers;

[ApiController]
[Authorize(Roles = "license_coordinator")]
[Route("api/internal/replacement-eligibility")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ReplacementEligibilityController(
    SupportDbContext db,
    IHttpClientFactory clients,
    IConfiguration config
) : ControllerBase
{
    private sealed record OrderItemData(
        Guid Id,
        Guid ProductId);

    private sealed record OrderData(
        Guid Id,
        string Status,
        List<OrderItemData> Items);

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
            ticket.OrderId is null ||
            ticket.LicenseId is null)
        {
            return Conflict(new
            {
                code = "TICKET_NOT_ELIGIBLE"
            });
        }

        var orderId = ticket.OrderId.Value;

        OrderData? order;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/internal/support/orders/{orderId:D}"
                + $"?customerId={Uri.EscapeDataString(ticket.CustomerId)}");

            if (!AuthenticationHeaderValue.TryParse(
                    Request.Headers.Authorization.ToString(),
                    out var auth) ||
                !string.Equals(
                    auth.Scheme, "Bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            request.Headers.Authorization = auth;

            request.Headers.Add(
                "X-Internal-Service-Key",
                config["InternalService:Key"]
                    ?? throw new InvalidOperationException(
                        "Internal service key missing"));

            using var response = await clients
                .CreateClient("SupportOrders")
                .SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            response.EnsureSuccessStatusCode();

            order = await response.Content
                .ReadFromJsonAsync<OrderData>(
                    cancellationToken: ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return StatusCode(503, new
            {
                code = "ORDER_SERVICE_UNAVAILABLE"
            });
        }

        if (order is null ||
            order.Id != ticket.OrderId ||
            order.Items is null)
        {
            return StatusCode(503, new
            {
                code = "INVALID_ORDER_RESPONSE"
            });
        }

        if (order.Status != "COMPLETED")
        {
            return Conflict(new
            {
                code = "ORDER_NOT_ELIGIBLE"
            });
        }

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
