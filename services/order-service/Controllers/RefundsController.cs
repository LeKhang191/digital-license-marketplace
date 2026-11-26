using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Authorize]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class RefundsController(
    OrderDbContext db
) : ControllerBase
{
    private string? UserId =>
        User.FindFirst("sub")?.Value;

    private static object Response(
        RefundOperation operation)
        => new
        {
            operation.OrderId,
            operation.RequestId,
            operation.RefundId,
            operation.Mode,
            operation.Status,
            operation.CreatedAtUtc,
            operation.UpdatedAtUtc,
            rejectionCode =
                operation.Status == "REJECTED"
                    ? operation.LastError
                    : null
        };

    private async Task<RefundOperation?> LockOperationAsync(
        Guid orderId,
        CancellationToken ct)
    {
        var rows = await db.RefundOperations
            .FromSqlInterpolated($"""
                SELECT *
                FROM refund_operations
                WHERE "OrderId" = {orderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        return rows.SingleOrDefault();
    }

    private async Task<Order?> LockOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        var rows = await db.Orders
            .FromSqlInterpolated($"""
                SELECT *
                FROM orders
                WHERE "Id" = {orderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        return rows.SingleOrDefault();
    }

    private async Task<bool> EligibleAsync(
        Order order,
        CancellationToken ct)
    {
        if (order.Status != OrderStatus.Completed ||
            order.PaidAtUtc is null ||
            order.TotalAmount <= 0 ||
            string.IsNullOrWhiteSpace(order.PaymentRef))
        {
            return false;
        }

        return await db.FulfillmentResults
            .AnyAsync(x =>
                x.OrderId == order.Id &&
                x.Status == "ASSIGNED", ct);
    }

    private static string TicketReason(Guid ticketId)
        => $"Verified defective license: ticket {ticketId:D}";

    private static bool MatchesTicket(
        RefundOperation operation,
        SupportTicket ticket)
        => operation.Mode == "VERIFIED_DEFECT" &&
           operation.DefectiveLicenseId == ticket.LicenseId &&
           operation.CustomerId == ticket.CustomerId &&
           operation.RequestedBy == ticket.HandledBy &&
           operation.Reason == TicketReason(ticket.Id);

    private static void Initialize(
        RefundOperation operation,
        Order order,
        string mode,
        string reason,
        string requestedBy,
        Guid? defectiveLicenseId)
    {
        var now = DateTime.UtcNow;

        operation.RequestId = Guid.NewGuid();
        operation.OrderId = order.Id;
        operation.CustomerId = order.KeycloakUserId;
        operation.PaymentRef = order.PaymentRef!;
        operation.Mode = mode;
        operation.Reason = reason;
        operation.RequestedBy = requestedBy;
        operation.DefectiveLicenseId = defectiveLicenseId;
        operation.Status = "REQUESTED";
        operation.RefundId = null;
        operation.CreatedAtUtc = now;
        operation.UpdatedAtUtc = now;
        operation.NextRetryAtUtc = now;
        operation.RetryCount = 0;
        operation.LastError = null;
    }

    [HttpPost("api/orders/{id:guid}/refund")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> RequestRefund(
        Guid id,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        // Check ownership before acquiring locks.
        if (!await db.Orders.AnyAsync(
            x => x.Id == id &&
                 x.KeycloakUserId == UserId, ct))
        {
            return NotFound();
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        // Same lock order as the orchestration worker.
        var existing = await LockOperationAsync(id, ct);
        var order = await LockOrderAsync(id, ct);

        if (order is null ||
            order.KeycloakUserId != UserId)
        {
            return NotFound();
        }

        // Another request might have inserted an operation
        // while this request was waiting for the order lock.
        if (existing is null)
        {
            existing = await db.RefundOperations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.OrderId == id, ct);
        }

        if (existing is not null)
        {
            await tx.CommitAsync(ct);
            return Ok(Response(existing));
        }

        if (!await EligibleAsync(order, ct))
        {
            return Conflict(new
            {
                code = "ORDER_NOT_REFUNDABLE"
            });
        }

        var operation = new RefundOperation();

        Initialize(
            operation,
            order,
            "UNREVEALED",
            "Customer requested refund before license disclosure",
            UserId!,
            null);

        db.RefundOperations.Add(operation);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Accepted(Response(operation));
    }

    [HttpGet("api/orders/{id:guid}/refund")]
    [Authorize(Policy = "CustomerOnly")]
    public async Task<IActionResult> GetRefund(
        Guid id,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        var operation = await db.RefundOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.OrderId == id &&
                x.CustomerId == UserId, ct);

        return operation is null
            ? NotFound()
            : Ok(Response(operation));
    }

    [HttpPost("api/support/tickets/{id:guid}/refund")]
    [Authorize(Roles = "license_coordinator")]
    public async Task<IActionResult> ApproveDefectRefund(
        Guid id,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        var candidate = await db.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.AssignedRole == "license_coordinator" &&
                x.HandledBy == UserId, ct);

        if (candidate is null)
            return NotFound();

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var existing = await LockOperationAsync(
            candidate.OrderId, ct);

        var order = await LockOrderAsync(
            candidate.OrderId, ct);

        if (order is null)
            return NotFound();

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
            ticket.OrderId != candidate.OrderId ||
            ticket.AssignedRole != "license_coordinator" ||
            ticket.HandledBy != UserId ||
            ticket.CustomerId != order.KeycloakUserId)
        {
            return NotFound();
        }

        if (existing is not null &&
            MatchesTicket(existing, ticket))
        {
            await tx.CommitAsync(ct);
            return Ok(Response(existing));
        }

        if (ticket.Status != "IN_PROGRESS" ||
            ticket.Category is not (
                "INVALID_KEY" or "KEY_ALREADY_USED") ||
            ticket.LicenseId is null ||
            ticket.LicenseId == Guid.Empty)
        {
            return Conflict(new
            {
                code = "TICKET_NOT_ELIGIBLE"
            });
        }

        if (existing is null)
        {
            // Recheck after acquiring the order lock.
            // Do not modify an operation that was not locked.
            var concurrent = await db.RefundOperations
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.OrderId == order.Id, ct);

            if (concurrent is not null)
            {
                return Conflict(new
                {
                    code = "REFUND_ALREADY_REQUESTED"
                });
            }
        }
        else
        {
            // A rejected unrevealed refund can be escalated
            // only through a verified defective-key ticket.
            if (existing.Status != "REJECTED" ||
                existing.Mode != "UNREVEALED" ||
                existing.LastError != "LICENSE_ALREADY_REVEALED")
            {
                return Conflict(new
                {
                    code = "REFUND_ALREADY_REQUESTED"
                });
            }
        }

        if (!await EligibleAsync(order, ct))
        {
            return Conflict(new
            {
                code = "ORDER_NOT_REFUNDABLE"
            });
        }

        // Full defect refund supports single-key orders only.
        if (await db.OrderItems.CountAsync(
                x => x.OrderId == order.Id, ct) != 1)
        {
            return Conflict(new
            {
                code = "DEFECT_REFUND_NOT_ELIGIBLE"
            });
        }

        var operation = existing ?? new RefundOperation();

        Initialize(
            operation,
            order,
            "VERIFIED_DEFECT",
            TicketReason(ticket.Id),
            UserId!,
            ticket.LicenseId);

        if (existing is null)
            db.RefundOperations.Add(operation);

        // Preserve the staff approval in the support history.
        db.SupportMessages.Add(new SupportMessage
        {
            TicketId = ticket.Id,
            SenderId = UserId!,
            SenderRole = "license_coordinator",
            Content =
                $"Defective license refund approved. Request: {operation.RequestId:D}"
        });

        ticket.Version = Guid.NewGuid();
        ticket.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Accepted(Response(operation));
    }

    [HttpGet("api/support/tickets/{id:guid}/refund")]
    [Authorize(Roles = "license_coordinator")]
    public async Task<IActionResult> GetSupportRefund(
        Guid id,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId))
            return Unauthorized();

        var ticket = await db.SupportTickets
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.Id == id &&
                x.AssignedRole == "license_coordinator" &&
                x.HandledBy == UserId, ct);

        if (ticket is null)
            return NotFound();

        var operation = await db.RefundOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == ticket.OrderId, ct);

        return operation is not null &&
               MatchesTicket(operation, ticket)
            ? Ok(Response(operation))
            : NotFound();
    }
}
