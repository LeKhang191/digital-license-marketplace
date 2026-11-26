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
[Route("api/support/tickets/{id:guid}/refund")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class SupportRefundsController(
    SupportDbContext db,
    IHttpClientFactory clients,
    IConfiguration config
) : ControllerBase
{
    private string? UserId =>
        User.FindFirst("sub")?.Value;

    private sealed record OrderReply(
        int Status,
        string Body);

    private async Task<OrderReply> CallOrderAsync(
        SupportTicket ticket,
        bool approve,
        CancellationToken ct)
    {
        var path =
            $"api/internal/support/tickets/{ticket.Id:D}/refund";

        if (!approve)
        {
            path +=
                $"?orderId={ticket.OrderId:D}" +
                $"&licenseId={ticket.LicenseId:D}" +
                $"&customerId={Uri.EscapeDataString(ticket.CustomerId)}";
        }

        using var request = new HttpRequestMessage(
            approve ? HttpMethod.Post : HttpMethod.Get,
            path);

        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var auth) ||
            !string.Equals(
                auth.Scheme, "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            return new OrderReply(401, "{}");
        }

        request.Headers.Authorization = auth;

        request.Headers.Add(
            "X-Internal-Service-Key",
            config["InternalService:Key"]
                ?? throw new InvalidOperationException(
                    "Missing internal service key"));

        if (approve)
        {
            request.Content = JsonContent.Create(new
            {
                ticket.Id,
                ticket.OrderId,
                ticket.LicenseId,
                ticket.CustomerId,
                ticket.HandledBy
            });
        }

        using var response = await clients
            .CreateClient("SupportOrders")
            .SendAsync(request, ct);

        var body = await response.Content
            .ReadAsStringAsync(ct);

        return new OrderReply(
            (int)response.StatusCode, body);
    }

    private IActionResult Forward(OrderReply reply)
    {
        if (reply.Status >= 500)
        {
            return StatusCode(503, new
            {
                code = "ORDER_SERVICE_UNAVAILABLE"
            });
        }

        return new ContentResult
        {
            StatusCode = reply.Status,
            ContentType = "application/json",
            Content = string.IsNullOrWhiteSpace(reply.Body)
                ? "{}" : reply.Body
        };
    }

    [HttpPost]
    public async Task<IActionResult> Approve(
        Guid id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var tickets = await db.SupportTickets
            .FromSqlInterpolated($"""
                SELECT *
                FROM support_tickets
                WHERE "Id" = {id}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var ticket = tickets.SingleOrDefault();

        if (ticket is null ||
            ticket.AssignedRole != "license_coordinator" ||
            ticket.HandledBy != UserId)
        {
            return NotFound();
        }

        if (ticket.LicenseId is null ||
            ticket.LicenseId == Guid.Empty ||
            ticket.Category is not
                ("INVALID_KEY" or "KEY_ALREADY_USED"))
        {
            return Conflict(new
            {
                code = "TICKET_NOT_ELIGIBLE"
            });
        }

        try
        {
            var eligible =
                ticket.Status == "IN_PROGRESS";

            // For an already resolved ticket, allow only
            // retrieval of an existing refund operation.
            var reply = await CallOrderAsync(
                ticket, eligible, ct);

            if (!eligible && reply.Status == 404)
            {
                return Conflict(new
                {
                    code = "TICKET_NOT_ELIGIBLE"
                });
            }

            if (reply.Status is not (200 or 202))
                return Forward(reply);

            using var json =
                JsonDocument.Parse(reply.Body);

            if (!json.RootElement.TryGetProperty(
                    "requestId", out var requestIdElement) ||
                !requestIdElement.TryGetGuid(
                    out var requestId) ||
                requestId == Guid.Empty)
            {
                throw new JsonException(
                    "Invalid refund request ID");
            }

            var content =
                $"Defective license refund approved. Request: {requestId:D}";

            var recorded = await db.SupportMessages
                .AnyAsync(x =>
                    x.TicketId == ticket.Id &&
                    x.SenderId == UserId &&
                    x.SenderRole == "license_coordinator" &&
                    x.Content == content, ct);

            if (!recorded)
            {
                db.SupportMessages.Add(
                    new SupportMessage
                    {
                        TicketId = ticket.Id,
                        SenderId = UserId!,
                        SenderRole = "license_coordinator",
                        Content = content
                    });

                ticket.Version = Guid.NewGuid();
                ticket.UpdatedAtUtc = DateTime.UtcNow;

                await db.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);

            return Forward(reply);
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
    }

    [HttpGet]
    public async Task<IActionResult> Get(
        Guid id, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        var ticket = await db.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.AssignedRole == "license_coordinator" &&
                x.HandledBy == UserId, ct);

        if (ticket is null ||
            ticket.LicenseId is null)
        {
            return NotFound();
        }

        try
        {
            return Forward(
                await CallOrderAsync(ticket, false, ct));
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
    }
}
