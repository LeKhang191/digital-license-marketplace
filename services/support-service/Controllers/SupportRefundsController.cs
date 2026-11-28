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
[Authorize(Roles = "license_coordinator,transaction_auditor")]
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

    private static bool IsDefectTicket(SupportTicket ticket)
        => ticket.AssignedRole == "license_coordinator" &&
           ticket.OrderId.HasValue &&
           ticket.LicenseId is not null &&
           ticket.LicenseId != Guid.Empty &&
           ticket.Category is ("INVALID_KEY" or "KEY_ALREADY_USED");

    private static bool IsUnrevealedTicket(SupportTicket ticket)
        => ticket.AssignedRole == "transaction_auditor" &&
           ticket.OrderId.HasValue &&
           ticket.OrderId != Guid.Empty &&
           ticket.Category == "REFUND_REQUEST";

    private async Task<OrderReply> CallOrderAsync(
        SupportTicket ticket,
        bool approve,
        CancellationToken ct)
    {
        var unrevealed = IsUnrevealedTicket(ticket);
        var path = unrevealed
            ? $"api/internal/support/tickets/{ticket.Id:D}/refund/unrevealed"
            : $"api/internal/support/tickets/{ticket.Id:D}/refund";

        if (!approve)
        {
            path += unrevealed
                ? $"?orderId={ticket.OrderId:D}&customerId={Uri.EscapeDataString(ticket.CustomerId)}"
                : $"?orderId={ticket.OrderId:D}&licenseId={ticket.LicenseId:D}&customerId={Uri.EscapeDataString(ticket.CustomerId)}";
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
            request.Content = unrevealed
                ? JsonContent.Create(new
                {
                    ticket.Id,
                    OrderId = ticket.OrderId!.Value,
                    ticket.CustomerId,
                    ticket.HandledBy
                })
                : JsonContent.Create(new
                {
                    ticket.Id,
                    OrderId = ticket.OrderId!.Value,
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
            !User.IsInRole(ticket.AssignedRole) ||
            ticket.HandledBy != UserId)
        {
            return NotFound();
        }

        if (!IsDefectTicket(ticket) && !IsUnrevealedTicket(ticket))
        {
            return Conflict(new
            {
                code = "TICKET_NOT_ELIGIBLE"
            });
        }

        try
        {
            var eligible = ticket.Status == "IN_PROGRESS";

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

            using var json = JsonDocument.Parse(reply.Body);

            if (!json.RootElement.TryGetProperty(
                    "requestId", out var requestIdElement) ||
                !requestIdElement.TryGetGuid(
                    out var requestId) ||
                requestId == Guid.Empty)
            {
                throw new JsonException("Invalid refund request ID");
            }

            var content = IsUnrevealedTicket(ticket)
                ? $"Unrevealed order refund approved. Request: {requestId:D}"
                : $"Defective license refund approved. Request: {requestId:D}";

            var recorded = await db.SupportMessages
                .AnyAsync(x =>
                    x.TicketId == ticket.Id &&
                    x.SenderId == UserId &&
                    x.SenderRole == ticket.AssignedRole &&
                    x.Content == content, ct);

            if (!recorded)
            {
                db.SupportMessages.Add(
                    new SupportMessage
                    {
                        TicketId = ticket.Id,
                        SenderId = UserId!,
                        SenderRole = ticket.AssignedRole,
                        Content = content
                    });

                ticket.ResolutionOutcome = "APPROVED_REFUND";
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
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.HandledBy == UserId, ct);

        if (ticket is null ||
            !User.IsInRole(ticket.AssignedRole) ||
            (!IsDefectTicket(ticket) && !IsUnrevealedTicket(ticket)))
        {
            return NotFound();
        }

        try
        {
            var reply = await CallOrderAsync(ticket, false, ct);

            if (reply.Status == 200 && !string.IsNullOrWhiteSpace(reply.Body))
            {
                try
                {
                    using var doc = JsonDocument.Parse(reply.Body);
                    var opStatus = doc.RootElement.TryGetProperty("status", out var stEl)
                        ? stEl.GetString()
                        : null;
                    var rejectionCode = doc.RootElement.TryGetProperty("rejectionCode", out var rcEl)
                        ? rcEl.GetString()
                        : null;

                    if (opStatus == "REJECTED" && ticket.ResolutionOutcome != "REJECTED")
                    {
                        var reason = rejectionCode switch
                        {
                            "LICENSE_ALREADY_REVEALED" =>
                                "Hệ thống tự động từ chối: Key bản quyền đã được mở xem trước khi hoàn tiền.",
                            "ORDER_ALREADY_COMPENSATED" =>
                                "Hệ thống tự động từ chối: Đơn hàng đã được đổi key mới trước đó.",
                            "DEFECT_REFUND_NOT_ELIGIBLE" =>
                                "Hệ thống tự động từ chối: Chỉ hỗ trợ hoàn toàn bộ đơn 1 key lỗi đã mở xem (đơn nhiều key vui lòng dùng Đổi key mới).",
                            _ => $"Hệ thống tự động từ chối hoàn tiền ({rejectionCode ?? "NOT_ELIGIBLE"})."
                        };

                        ticket.ResolutionOutcome = "REJECTED";
                        ticket.RejectionReason = reason;
                        if (ticket.Status == "IN_PROGRESS")
                            ticket.Status = "RESOLVED";
                        ticket.Version = Guid.NewGuid();
                        ticket.UpdatedAtUtc = DateTime.UtcNow;

                        db.SupportMessages.Add(new SupportMessage
                        {
                            TicketId = ticket.Id,
                            SenderId = UserId!,
                            SenderRole = ticket.AssignedRole,
                            Content = $"[TỰ ĐỘNG TỪ CHỐI HOÀN TIỀN] {reason}"
                        });

                        await db.SaveChangesAsync(ct);
                    }
                    else if (opStatus == "COMPLETED" && ticket.Status == "IN_PROGRESS")
                    {
                        ticket.ResolutionOutcome = "APPROVED_REFUND";
                        ticket.Status = "RESOLVED";
                        ticket.Version = Guid.NewGuid();
                        ticket.UpdatedAtUtc = DateTime.UtcNow;

                        var doneContent = "[HOÀN TIỀN THÀNH CÔNG] Tiền đã được tự động hoàn vào Ví DLM của khách hàng.";
                        if (!await db.SupportMessages.AnyAsync(
                                m => m.TicketId == ticket.Id && m.Content == doneContent, ct))
                        {
                            db.SupportMessages.Add(new SupportMessage
                            {
                                TicketId = ticket.Id,
                                SenderId = UserId!,
                                SenderRole = ticket.AssignedRole,
                                Content = doneContent
                            });
                        }

                        await db.SaveChangesAsync(ct);
                    }
                }
                catch (JsonException)
                {
                    // Ignore malformed sync preview and forward raw response.
                }
            }

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
}
