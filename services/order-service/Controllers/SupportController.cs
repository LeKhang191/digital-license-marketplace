using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Authorize]
[Route("api/support/tickets")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportController(
    OrderDbContext db,
    IHttpClientFactory clients
) : ControllerBase
{
    private string? UserId => User.FindFirst("sub")?.Value;

    private bool IsCustomer =>
        User.IsInRole("customer") &&
        !User.IsInRole("catalog_manager") &&
        !User.IsInRole("license_coordinator") &&
        !User.IsInRole("transaction_auditor") &&
        !User.IsInRole("admin");

    private bool IsStaff =>
        User.IsInRole("license_coordinator") ||
        User.IsInRole("transaction_auditor");

    private bool CanAccess =>
        !string.IsNullOrWhiteSpace(UserId) &&
        (IsCustomer || IsStaff);

    private static readonly Dictionary<string, string> Categories = new()
    {
        ["INVALID_KEY"] = "license_coordinator",
        ["KEY_ALREADY_USED"] = "license_coordinator",
        ["KEY_NOT_RECEIVED"] = "license_coordinator",
        ["PAYMENT_ISSUE"] = "transaction_auditor",
        ["REFUND_REQUEST"] = "transaction_auditor",
        ["OTHER"] = "transaction_auditor"
    };

    private sealed record OwnedLicense(
        Guid Id,
        Guid OrderId,
        Guid OrderItemId,
        Guid ProductId
    );

    private IQueryable<SupportTicket> AccessibleTickets()
    {
        var query = db.SupportTickets.AsQueryable();

        if (IsCustomer)
            return query.Where(x => x.CustomerId == UserId);

        var coordinator = User.IsInRole("license_coordinator");
        var auditor = User.IsInRole("transaction_auditor");

        return query.Where(x =>
            (coordinator && x.AssignedRole == "license_coordinator") ||
            (auditor && x.AssignedRole == "transaction_auditor"));
    }

    private static bool ValidText(string? value, int min, int max)
        => !string.IsNullOrWhiteSpace(value) &&
           value.Trim().Length >= min &&
           value.Trim().Length <= max;

    private static bool AllowedOrderStatus(
        string category, string status)
        => category switch
        {
            "INVALID_KEY" or "KEY_ALREADY_USED"
                => status == OrderStatus.Completed,

            "KEY_NOT_RECEIVED"
                => status == OrderStatus.Paid,

            "PAYMENT_ISSUE"
                => status == OrderStatus.Pending ||
                   status == OrderStatus.Paid ||
                   status == OrderStatus.Completed,

            "REFUND_REQUEST"
                => status == OrderStatus.Paid ||
                   status == OrderStatus.Completed,

            "OTHER" => true,

            _ => false
        };

    private static bool NeedsLicense(string category)
        => category is "INVALID_KEY" or "KEY_ALREADY_USED";

    private async Task<bool> VerifyLicenseAsync(
        Guid licenseId, Order order, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/licenses/me?orderId={order.Id}");

        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var auth) ||
            !string.Equals(auth.Scheme, "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Missing bearer token");
        }

        request.Headers.Authorization = auth;

        using var response = await clients
            .CreateClient("SupportLicenses")
            .SendAsync(request, ct);

        response.EnsureSuccessStatusCode();

        var licenses = await response.Content
            .ReadFromJsonAsync<List<OwnedLicense>>(
                cancellationToken: ct)
            ?? throw new JsonException("Invalid license response");

        return licenses.Any(x =>
            x.Id == licenseId &&
            x.OrderId == order.Id &&
            order.Items.Any(i =>
                i.Id == x.OrderItemId &&
                i.ProductId == x.ProductId));
    }

    private IActionResult Error(
        int status, string code, string message)
        => StatusCode(status, new { code, error = message });

    private IActionResult Changed()
        => Error(409, "TICKET_CHANGED",
            "Ticket has changed. Refresh and retry.");

    private static void Touch(SupportTicket ticket)
    {
        ticket.Version = Guid.NewGuid();
        ticket.UpdatedAtUtc = DateTime.UtcNow;
    }

    // POST /api/support/tickets
    [HttpPost]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> Create(
        CreateSupportTicketRequest request,
        CancellationToken ct)
    {
        if (UserId is null)
            return Unauthorized();

        var category = request.Category?.Trim().ToUpperInvariant();

        if (category is null ||
            !Categories.TryGetValue(category, out var assignedRole) ||
            request.OrderId == Guid.Empty ||
            !ValidText(request.Subject, 3, 150) ||
            !ValidText(request.Description, 5, 3000))
        {
            return Error(400, "INVALID_TICKET",
                "Invalid ticket information.");
        }

        var order = await db.Orders
            .AsNoTracking()
            .Include(x => x.Items)
            .SingleOrDefaultAsync(x =>
                x.Id == request.OrderId &&
                x.KeycloakUserId == UserId, ct);

        if (order is null)
            return NotFound();

        if (!AllowedOrderStatus(category, order.Status))
            return Error(409, "INVALID_ORDER_STATUS",
                "Order status does not allow this request.");

        if (NeedsLicense(category) &&
            (!request.LicenseId.HasValue ||
             request.LicenseId.Value == Guid.Empty))
        {
            return Error(400, "LICENSE_REQUIRED",
                "License ID is required.");
        }

        if (request.LicenseId.HasValue)
        {
            if (order.Status != OrderStatus.Completed)
                return Error(409, "INVALID_ORDER_STATUS",
                    "Order has no eligible delivered license.");

            try
            {
                if (!await VerifyLicenseAsync(
                    request.LicenseId.Value, order, ct))
                    return NotFound();
            }
            catch (Exception ex) when (
                ex is HttpRequestException or JsonException ||
                (ex is OperationCanceledException &&
                 !ct.IsCancellationRequested))
            {
                return Error(503, "LICENSE_SERVICE_UNAVAILABLE",
                    "Unable to verify license ownership.");
            }
        }

        var duplicate = await db.SupportTickets.AnyAsync(x =>
            x.OrderId == order.Id &&
            x.Category == category &&
            (x.Status == "OPEN" ||
             x.Status == "IN_PROGRESS"), ct);

        if (duplicate)
            return Error(409, "DUPLICATE_TICKET",
                "An active ticket already exists.");

        var ticket = new SupportTicket
        {
            OrderId = order.Id,
            LicenseId = request.LicenseId,
            CustomerId = UserId,
            Category = category,
            Subject = request.Subject.Trim(),
            Description = request.Description.Trim(),
            AssignedRole = assignedRole
        };

        db.SupportTickets.Add(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg &&
            pg.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            return Error(409, "DUPLICATE_TICKET",
                "An active ticket already exists.");
        }

        return CreatedAtAction(
            nameof(Get), new { id = ticket.Id }, ticket);
    }

    // GET /api/support/tickets
    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!CanAccess)
            return Forbid();

        if (page < 1 || page > 10000 ||
            pageSize < 1 || pageSize > 100)
            return BadRequest();

        var query = AccessibleTickets().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(status))
        {
            status = status.Trim().ToUpperInvariant();

            if (status is not (
                "OPEN" or "IN_PROGRESS" or "RESOLVED" or "CLOSED"))
                return BadRequest();

            query = query.Where(x => x.Status == status);
        }

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.UpdatedAtUtc)
            .ThenBy(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                x.Id,
                x.OrderId,
                x.LicenseId,
                x.Category,
                x.Subject,
                x.Status,
                x.AssignedRole,
                x.CreatedAtUtc,
                x.UpdatedAtUtc
            })
            .ToListAsync(ct);

        return Ok(new { items, total, page, pageSize });
    }

    // GET /api/support/tickets/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(
        Guid id, CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        var ticket = await AccessibleTickets()
            .AsNoTracking()
            .Include(x => x.Messages
                .OrderBy(m => m.CreatedAtUtc)
                .ThenBy(m => m.Id))
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        return ticket is null ? NotFound() : Ok(ticket);
    }

    // POST /api/support/tickets/{id}/messages
    [HttpPost("{id:guid}/messages")]
    public async Task<IActionResult> AddMessage(
        Guid id,
        AddSupportMessageRequest request,
        CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        if (!ValidText(request.Content, 1, 3000))
            return Error(400, "INVALID_MESSAGE",
                "Invalid message content.");

        var ticket = await AccessibleTickets()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (ticket.Status is "RESOLVED" or "CLOSED")
            return Error(409, "TICKET_NOT_ACTIVE",
                "Ticket no longer accepts messages.");

        if (!IsCustomer &&
            (ticket.Status != "IN_PROGRESS" ||
             ticket.HandledBy != UserId))
            return Forbid();

        var message = new SupportMessage
        {
            TicketId = ticket.Id,
            SenderId = UserId!,
            SenderRole = IsCustomer
                ? "customer"
                : ticket.AssignedRole,
            Content = request.Content.Trim()
        };

        db.SupportMessages.Add(message);
        Touch(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        return Ok(message);
    }

    // PATCH /api/support/tickets/{id}/status
    [HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> UpdateStatus(
        Guid id,
        UpdateSupportStatusRequest request,
        CancellationToken ct)
    {
        if (!CanAccess)
            return Forbid();

        var next = request.Status?.Trim().ToUpperInvariant();

        if (next is not ("IN_PROGRESS" or "RESOLVED" or "CLOSED"))
            return BadRequest();

        var ticket = await AccessibleTickets()
            .SingleOrDefaultAsync(x => x.Id == id, ct);

        if (ticket is null)
            return NotFound();

        if (IsCustomer)
        {
            if (next != "CLOSED")
                return Forbid();

            if (ticket.Status != "RESOLVED")
                return Error(409, "INVALID_TRANSITION",
                    "Only resolved tickets can be closed.");
        }
        else
        {
            if (next == "CLOSED")
                return Forbid();

            if (ticket.Status == "OPEN" &&
                next == "IN_PROGRESS")
            {
                ticket.HandledBy = UserId;
            }
            else if (ticket.Status == "IN_PROGRESS" &&
                     next == "RESOLVED")
            {
                if (ticket.HandledBy != UserId)
                    return Forbid();
            }
            else
            {
                return Error(409, "INVALID_TRANSITION",
                    "Invalid ticket status transition.");
            }
        }

        ticket.Status = next;
        Touch(ticket);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Changed();
        }

        return Ok(new
        {
            ticket.Id,
            ticket.Status,
            ticket.HandledBy,
            ticket.UpdatedAtUtc
        });
    }
}
