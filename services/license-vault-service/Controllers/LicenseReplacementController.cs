using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Authorize(Policy = "LicenseCoordinatorOnly")]
[Route("api/licenses/replacements")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class LicenseReplacementController(
    LicenseDbContext db,
    IHttpClientFactory clients
) : ControllerBase
{
    private sealed record OrderItem(
        Guid Id,
        Guid ProductId
    );

    private sealed record Eligibility(
        Guid TicketId,
        Guid OrderId,
        Guid LicenseId,
        string CustomerId,
        string HandledBy,
        List<OrderItem> Items
    );

    private string? StaffId => User.FindFirst("sub")?.Value;

    private IActionResult Error(
        int status, string code, string message)
        => StatusCode(status, new { code, error = message });

    private static object Result(
        LicenseReplacement replacement,
        bool alreadyProcessed)
        => new
        {
            replacement.TicketId,
            replacement.OrderId,
            replacement.OrderItemId,
            replacement.ProductId,
            replacement.OldLicenseId,
            replacement.NewLicenseId,
            replacement.HandledBy,
            replacement.CreatedAtUtc,
            alreadyProcessed
        };

    [HttpGet("{ticketId:guid}")]
    public async Task<IActionResult> Get(
        Guid ticketId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(StaffId))
            return Unauthorized();

        var replacement = await db.LicenseReplacements
            .AsNoTracking()
            .SingleOrDefaultAsync(x =>
                x.TicketId == ticketId &&
                x.HandledBy == StaffId, ct);

        return replacement is null
            ? NotFound()
            : Ok(Result(replacement, true));
    }

    [HttpPost("{ticketId:guid}")]
    public async Task<IActionResult> Replace(
        Guid ticketId,
        CancellationToken ct)
    {
        var staffId = StaffId;

        if (string.IsNullOrWhiteSpace(staffId))
            return Unauthorized();

        if (ticketId == Guid.Empty)
            return BadRequest();

        // Return a previously completed operation safely.
        var previous = await db.LicenseReplacements
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TicketId == ticketId, ct);

        if (previous is not null)
        {
            return previous.HandledBy == staffId
                ? Ok(Result(previous, true))
                : NotFound();
        }

        Eligibility? eligibility;

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"api/internal/replacement-eligibility/{ticketId}");

            if (!AuthenticationHeaderValue.TryParse(
                    Request.Headers.Authorization.ToString(),
                    out var auth) ||
                !string.Equals(
                    auth.Scheme,
                    "Bearer",
                    StringComparison.OrdinalIgnoreCase))
            {
                return Unauthorized();
            }

            request.Headers.Authorization = auth;

            using var response = await clients
                .CreateClient("LicenseSupport")
                .SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.NotFound)
                return NotFound();

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                return Error(
                    409,
                    "REPLACEMENT_NOT_ELIGIBLE",
                    "Ticket or order is not eligible.");
            }

            response.EnsureSuccessStatusCode();

            eligibility = await response.Content
                .ReadFromJsonAsync<Eligibility>(
                    cancellationToken: ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return Error(
                503,
                "SUPPORT_SERVICE_UNAVAILABLE",
                "Cannot verify replacement eligibility.");
        }

        if (eligibility is null ||
            eligibility.TicketId != ticketId ||
            eligibility.HandledBy != staffId ||
            eligibility.OrderId == Guid.Empty ||
            eligibility.LicenseId == Guid.Empty ||
            string.IsNullOrWhiteSpace(eligibility.CustomerId) ||
            eligibility.Items is null)
        {
            return Error(
                503,
                "INVALID_ORDER_RESPONSE",
                "Invalid eligibility response.");
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        await LicenseOrderLock.AcquireAsync(
            db, eligibility.OrderId, ct);

        if (await db.LicenseRefundGuards.AnyAsync(
            x => x.OrderId == eligibility.OrderId, ct))
        {
            return Error(
                409,
                "LICENSE_REFUND_LOCKED",
                "Order is protected by a refund.");
        }

        // Serialize retries for the same ticket.
        var lockKey = $"replacement:{ticketId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """,
            ct);

        // Recheck after acquiring the transaction lock.
        previous = await db.LicenseReplacements
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TicketId == ticketId, ct);

        if (previous is not null)
        {
            await transaction.CommitAsync(ct);

            return previous.HandledBy == staffId
                ? Ok(Result(previous, true))
                : NotFound();
        }

        // Lock and verify the original license.
        var oldRows = await db.LicenseKeys
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM license_keys
                WHERE "Id" = {eligibility.LicenseId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var oldLicense = oldRows.SingleOrDefault();

        if (oldLicense is null ||
            oldLicense.OrderId != eligibility.OrderId ||
            oldLicense.CustomerId != eligibility.CustomerId)
        {
            return NotFound();
        }

        if (oldLicense.Status != LicenseStatus.Assigned ||
            oldLicense.OrderItemId is null)
        {
            return Error(
                409,
                "LICENSE_NOT_REPLACEABLE",
                "License is no longer assigned.");
        }

        var itemId = oldLicense.OrderItemId.Value;
        var productId = oldLicense.ProductId;

        if (!eligibility.Items.Any(x =>
                x.Id == itemId &&
                x.ProductId == productId))
        {
            return Error(
                409,
                "ORDER_ITEM_MISMATCH",
                "License does not match the order item.");
        }

        // Find a replacement for the exact same product.
        var available = await db.LicenseKeys
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM license_keys
                WHERE "ProductId" = {productId}
                  AND "Status" = {LicenseStatus.Available}
                  AND "OrderId" IS NULL
                  AND "OrderItemId" IS NULL
                  AND "CustomerId" IS NULL
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var newLicense = available.SingleOrDefault();

        if (newLicense is null)
        {
            return Error(
                409,
                "REPLACEMENT_OUT_OF_STOCK",
                "No replacement license is available.");
        }

        var now = DateTime.UtcNow;

        // Release the unique OrderItemId reference first.
        // Keep the previous key permanently disabled.
        oldLicense.Status = LicenseStatus.Disabled;
        oldLicense.OrderItemId = null;

        await db.SaveChangesAsync(ct);

        // Transfer the existing order item to the new key.
        newLicense.Status = LicenseStatus.Assigned;
        newLicense.OrderId = eligibility.OrderId;
        newLicense.OrderItemId = itemId;
        newLicense.CustomerId = eligibility.CustomerId;
        newLicense.AssignedAtUtc = now;

        var replacement = new LicenseReplacement
        {
            TicketId = ticketId,
            OrderId = eligibility.OrderId,
            OrderItemId = itemId,
            ProductId = productId,
            OldLicenseId = oldLicense.Id,
            NewLicenseId = newLicense.Id,
            CustomerId = eligibility.CustomerId,
            HandledBy = staffId,
            CreatedAtUtc = now
        };

        db.LicenseReplacements.Add(replacement);

        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        return Ok(Result(replacement, false));
    }
}
