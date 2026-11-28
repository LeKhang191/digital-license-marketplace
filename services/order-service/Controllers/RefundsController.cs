using System.Text.Json;
using Contracts.Events;
using Marketplace.Shared.Security;
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
    OrderDbContext db,
    IConfiguration config
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

    public sealed record DefectRefundRequest(
        Guid Id,
        Guid OrderId,
        Guid? LicenseId,
        string CustomerId,
        string HandledBy
    );

    private static string TicketReason(Guid ticketId)
        => $"Verified defective license: ticket {ticketId:D}";

    private static bool MatchesTicket(
        RefundOperation operation,
        DefectRefundRequest ticket)
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

    [HttpPost("api/internal/support/tickets/{id:guid}/refund")]
    [Authorize(Roles = "license_coordinator")]
    public async Task<IActionResult> ApproveDefectRefund(
        Guid id,
        DefectRefundRequest ticket,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            !new InternalServiceAuth(config).Validate(Request))
        {
            return Unauthorized();
        }

        if (ticket.Id != id ||
            ticket.OrderId == Guid.Empty ||
            ticket.LicenseId is null ||
            ticket.LicenseId == Guid.Empty ||
            ticket.HandledBy != UserId ||
            string.IsNullOrWhiteSpace(ticket.CustomerId))
        {
            return BadRequest();
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var existing = await LockOperationAsync(
            ticket.OrderId, ct);

        var order = await LockOrderAsync(
            ticket.OrderId, ct);

        if (order is null ||
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

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Accepted(Response(operation));
    }

    [HttpGet("api/internal/support/tickets/{id:guid}/refund")]
    [Authorize(Roles = "license_coordinator")]
    public async Task<IActionResult> GetSupportRefund(
        Guid id,
        [FromQuery] Guid orderId,
        [FromQuery] Guid licenseId,
        [FromQuery] string customerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            !new InternalServiceAuth(config).Validate(Request))
        {
            return Unauthorized();
        }

        var ticket = new DefectRefundRequest(
            id, orderId, licenseId, customerId, UserId!);

        var operation = await db.RefundOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId, ct);

        return operation is not null &&
               MatchesTicket(operation, ticket)
            ? Ok(Response(operation))
            : NotFound();
    }

    public sealed record UnrevealedSupportRefundRequest(
        Guid Id,
        Guid OrderId,
        string CustomerId,
        string HandledBy
    );

    public sealed record RejectSupportRefundNotifyRequest(
        Guid TicketId,
        Guid OrderId,
        string CustomerId,
        string Reason
    );

    private static string UnrevealedTicketReason(Guid ticketId)
        => $"Approved unrevealed refund: ticket {ticketId:D}";

    private static bool MatchesUnrevealedTicket(
        RefundOperation operation,
        UnrevealedSupportRefundRequest ticket)
        => operation.Mode == "UNREVEALED" &&
           operation.CustomerId == ticket.CustomerId &&
           operation.RequestedBy == ticket.HandledBy &&
           operation.Reason == UnrevealedTicketReason(ticket.Id);

    [HttpPost("api/internal/support/tickets/{id:guid}/refund/unrevealed")]
    [Authorize(Roles = "transaction_auditor")]
    public async Task<IActionResult> ApproveUnrevealedSupportRefund(
        Guid id,
        UnrevealedSupportRefundRequest ticket,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            !new InternalServiceAuth(config).Validate(Request))
        {
            return Unauthorized();
        }

        if (ticket.Id != id ||
            ticket.OrderId == Guid.Empty ||
            ticket.HandledBy != UserId ||
            string.IsNullOrWhiteSpace(ticket.CustomerId))
        {
            return BadRequest();
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var existing = await LockOperationAsync(ticket.OrderId, ct);
        var order = await LockOrderAsync(ticket.OrderId, ct);

        if (order is null || ticket.CustomerId != order.KeycloakUserId)
            return NotFound();

        if (existing is not null && MatchesUnrevealedTicket(existing, ticket))
        {
            await tx.CommitAsync(ct);
            return Ok(Response(existing));
        }

        if (existing is null)
        {
            var concurrent = await db.RefundOperations
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.OrderId == order.Id, ct);

            if (concurrent is not null)
            {
                return Conflict(new
                {
                    code = concurrent.Status == "REJECTED"
                        ? (concurrent.LastError ?? "REFUND_ALREADY_REJECTED")
                        : "REFUND_ALREADY_REQUESTED"
                });
            }
        }
        else
        {
            return Conflict(new
            {
                code = existing.Status == "REJECTED"
                    ? (existing.LastError ?? "REFUND_ALREADY_REJECTED")
                    : "REFUND_ALREADY_REQUESTED"
            });
        }

        if (!await EligibleAsync(order, ct))
        {
            return Conflict(new { code = "ORDER_NOT_REFUNDABLE" });
        }

        var maxDays = config.GetValue("RefundPolicy:UnrevealedWindowDays", 7);
        if (maxDays > 0 &&
            order.PaidAtUtc is not null &&
            DateTime.UtcNow - order.PaidAtUtc.Value > TimeSpan.FromDays(maxDays))
        {
            return Conflict(new
            {
                code = "REFUND_WINDOW_EXPIRED",
                maxDays
            });
        }

        var operation = new RefundOperation();
        Initialize(
            operation,
            order,
            "UNREVEALED",
            UnrevealedTicketReason(ticket.Id),
            UserId!,
            null);

        db.RefundOperations.Add(operation);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return Accepted(Response(operation));
    }

    [HttpGet("api/internal/support/tickets/{id:guid}/refund/unrevealed")]
    [Authorize(Roles = "transaction_auditor")]
    public async Task<IActionResult> GetUnrevealedSupportRefund(
        Guid id,
        [FromQuery] Guid orderId,
        [FromQuery] string customerId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            !new InternalServiceAuth(config).Validate(Request))
        {
            return Unauthorized();
        }

        var ticket = new UnrevealedSupportRefundRequest(
            id, orderId, customerId, UserId!);

        var operation = await db.RefundOperations
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.OrderId == orderId, ct);

        return operation is not null && MatchesUnrevealedTicket(operation, ticket)
            ? Ok(Response(operation))
            : NotFound();
    }

    [HttpPost("api/internal/support/tickets/{id:guid}/refund/reject-notify")]
    [Authorize(Roles = "license_coordinator,transaction_auditor")]
    public async Task<IActionResult> NotifyRefundRejected(
        Guid id,
        RejectSupportRefundNotifyRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(UserId) ||
            !new InternalServiceAuth(config).Validate(Request))
        {
            return Unauthorized();
        }

        if (request.TicketId != id ||
            request.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            string.IsNullOrWhiteSpace(request.Reason))
        {
            return BadRequest();
        }

        var order = await db.Orders
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.Id == request.OrderId && x.KeycloakUserId == request.CustomerId,
                ct);

        if (order is null)
            return NotFound();

        var alreadyQueued = await db.OutboxMessages.AnyAsync(
            x => x.AggregateId == request.TicketId &&
                 x.RoutingKey == EventRoutes.RefundRejected,
            ct);

        if (!alreadyQueued)
        {
            var reason = request.Reason.Trim();
            if (reason.Length > 500)
                reason = reason[..500];

            db.OutboxMessages.Add(new OutboxMessage
            {
                AggregateId = request.TicketId,
                RoutingKey = EventRoutes.RefundRejected,
                Payload = JsonSerializer.Serialize(
                    new OrderRefundRejectedEvent(
                        order.Id,
                        order.KeycloakUserId,
                        order.CustomerEmail,
                        order.TotalAmount,
                        order.CustomerEmailVerified,
                        reason),
                    new JsonSerializerOptions(JsonSerializerDefaults.Web)),
                CreatedAtUtc = DateTime.UtcNow,
                NextRetryAtUtc = DateTime.UtcNow
            });

            await db.SaveChangesAsync(ct);
        }

        return Ok(new { queued = true });
    }
}
