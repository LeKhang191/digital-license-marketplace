using System.Security.Cryptography;
using System.Text.Json;
using Contracts.Events;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Application.Services;

public sealed class LicenseAllocationService(
    LicenseDbContext db,
    ILogger<LicenseAllocationService> log
) 
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public async Task ProcessAsync(
        OrderPaidEvent message,
        CancellationToken ct)
    {
        Validate(message);

        var fingerprint = GetFingerprint(message);

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        // Shared lock for allocation and refund.
        await LicenseOrderLock.AcquireAsync(
            db, message.OrderId, ct);

        var previous = await db.AllocationResults
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrderId == message.OrderId,
                ct);

        if (previous is not null)
        {
            if (previous.RequestFingerprint != fingerprint)
            {
                throw new InvalidDataException(
                    "Conflicting event for the same order.");
            }

            await transaction.CommitAsync(ct);

            log.LogInformation(
                "Order {OrderId} already processed",
                message.OrderId);

            return;
        }

        if (await db.LicenseRefundGuards.AnyAsync(
            x => x.OrderId == message.OrderId, ct))
        {
            throw new InvalidDataException(
                "Order is protected by a refund guard.");
        }

        var selected = new List<(
            OrderItemRef Item,
            LicenseKey License
        )>();

        Guid? missingProduct = null;

        // Consistent product order prevents lock-order conflicts.
        var groups = message.Items
            .GroupBy(i => i.ProductId)
            .OrderBy(g => g.Key);

        foreach (var group in groups)
        {
            var productId = group.Key;

            var items = group
                .OrderBy(i => i.ItemId)
                .ToArray();

            var quantity = items.Length;

            var available = await db.LicenseKeys
                .FromSqlInterpolated(
                    $"""
                    SELECT *
                    FROM license_keys
                    WHERE "ProductId" = {productId}
                      AND "Status" = {LicenseStatus.Available}
                    ORDER BY "CreatedAtUtc", "Id"
                    LIMIT {quantity}
                    FOR UPDATE
                    """)
                .ToListAsync(ct);

            if (available.Count != quantity)
            {
                missingProduct = productId;
                break;
            }

            for (int i = 0; i < quantity; i++)
            {
                selected.Add((
                    items[i],
                    available[i]
                ));
            }
        }

        var now = DateTime.UtcNow;

        var result = new AllocationResult
        {
            OrderId = message.OrderId,
            CustomerId = message.KeycloakUserId,
            RequestFingerprint = fingerprint,
            ProcessedAtUtc = now,
            NextRetryAtUtc = now
        };

        if (missingProduct.HasValue)
        {
            // No license has been modified yet.
            result.Status = "OUT_OF_STOCK";
            result.RoutingKey = EventRoutes.KeyOutOfStock;

            result.Payload = JsonSerializer.Serialize(
                new KeyOutOfStockEvent(
                    message.OrderId,
                    missingProduct.Value,
                    "Insufficient license stock"
                ),
                Json);
        }
        else
        {
            var assigned = new List<AssignedKey>();

            foreach (var pair in selected)
            {
                var license = pair.License;

                license.Status = LicenseStatus.Assigned;

                license.OrderId = message.OrderId;
                license.OrderItemId = pair.Item.ItemId;
                license.CustomerId = message.KeycloakUserId;
                license.AssignedAtUtc = now;

                assigned.Add(new AssignedKey(
                    pair.Item.ItemId,
                    pair.Item.ProductId,
                    license.Id
                ));
            }

            result.Status = "ASSIGNED";
            result.RoutingKey = EventRoutes.KeyAssigned;

            result.Payload = JsonSerializer.Serialize(
                new KeyAssignedEvent(
                    message.OrderId,
                    message.KeycloakUserId,
                    message.CustomerEmail,
                    assigned
                ),
                Json);
        }

        db.AllocationResults.Add(result);

        // License assignment and event result are atomic.
        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        log.LogInformation(
            "Order {OrderId} allocation result: {Status}",
            message.OrderId,
            result.Status);
    }

    private static string GetFingerprint(
        OrderPaidEvent message)
    {
        var normalized = message with
        {
            Items = message.Items
                .OrderBy(i => i.ItemId)
                .ToArray()
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(
            normalized, Json);

        return Convert.ToHexString(
            SHA256.HashData(bytes)
        ).ToLowerInvariant();
    }

    private static void Validate(OrderPaidEvent message)
    {
        if (message.OrderId == Guid.Empty ||
            string.IsNullOrWhiteSpace(
                message.KeycloakUserId) ||
            message.KeycloakUserId.Length > 255 ||
            message.Amount <= 0 ||
            message.Items is null ||
            message.Items.Count == 0 ||
            message.Items.Count > 10)
        {
            throw new InvalidDataException(
                "Invalid OrderPaidEvent.");
        }

        if (message.Items.Any(i =>
            i is null ||
            i.ItemId == Guid.Empty ||
            i.ProductId == Guid.Empty))
        {
            throw new InvalidDataException(
                "Invalid order items.");
        }

        if (message.Items
            .Select(i => i.ItemId)
            .Distinct()
            .Count() != message.Items.Count)
        {
            throw new InvalidDataException(
                "Duplicate order item IDs.");
        }
    }
}
