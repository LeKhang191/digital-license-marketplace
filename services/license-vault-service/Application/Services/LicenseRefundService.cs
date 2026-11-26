using System.Text.Json;
using Contracts.Events;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Application.Services;

public sealed record LicenseRefundRequest(
    Guid RequestId,
    Guid OrderId,
    string CustomerId,
    string Mode,
    Guid? DefectiveLicenseId = null);

public sealed record LicenseRefundResult(
    Guid OrderId,
    Guid RequestId,
    string Status);

public sealed class LicenseRefundService(
    LicenseDbContext db)
{
    private static LicenseRefundResult Result(
        LicenseRefundGuard guard)
        => new(
            guard.OrderId,
            guard.RequestId,
            guard.Status);

    public async Task<(
        LicenseRefundResult? Result,
        string? Error)> PrepareAsync(
        LicenseRefundRequest request,
        CancellationToken ct)
    {
        if (request.OrderId == Guid.Empty ||
            request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.CustomerId) ||
            request.CustomerId.Length > 255 ||
            request.Mode is not (
                "UNREVEALED" or
                "OUT_OF_STOCK" or
                "VERIFIED_DEFECT"))
        {
            return (null, "INVALID_REFUND_REQUEST");
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        await LicenseOrderLock.AcquireAsync(
            db, request.OrderId, ct);

        var existing = await db.LicenseRefundGuards
            .SingleOrDefaultAsync(
                x => x.OrderId == request.OrderId, ct);

        if (existing is not null)
        {
            if (existing.RequestId != request.RequestId ||
                existing.CustomerId != request.CustomerId ||
                existing.Mode != request.Mode)
            {
                return (null, "REFUND_GUARD_CONFLICT");
            }

            await tx.CommitAsync(ct);

            return (Result(existing), null);
        }

        var allocation = await db.AllocationResults
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.OrderId == request.OrderId, ct);

        if (allocation is null ||
            allocation.CustomerId != request.CustomerId)
        {
            return (null, "ALLOCATION_NOT_FOUND");
        }

        var keys = await db.LicenseKeys
            .FromSqlInterpolated($"""
                SELECT *
                FROM license_keys
                WHERE "OrderId" = {request.OrderId}
                  AND "Status" = {LicenseStatus.Assigned}
                ORDER BY "Id"
                FOR UPDATE
                """)
            .ToListAsync(ct);

        if (keys.Any(
            k => k.CustomerId != request.CustomerId ||
                 k.OrderItemId is null))
        {
            return (null, "LICENSE_OWNER_CONFLICT");
        }

        if (request.Mode == "OUT_OF_STOCK")
        {
            if (allocation.Status != "OUT_OF_STOCK" ||
                keys.Count != 0)
            {
                return (null, "REFUND_NOT_ELIGIBLE");
            }
        }
        else
        {
            if (allocation.Status != "ASSIGNED" ||
                keys.Count == 0)
            {
                return (null, "REFUND_NOT_ELIGIBLE");
            }

            var assigned =
                JsonSerializer.Deserialize<KeyAssignedEvent>(
                    allocation.Payload,
                    new JsonSerializerOptions(
                        JsonSerializerDefaults.Web));

            if (assigned is null ||
                assigned.Keys is null ||
                keys.Count != assigned.Keys.Count ||
                !keys.Select(k => (
                        k.OrderItemId!.Value,
                        k.ProductId))
                    .OrderBy(k => k.Item1)
                    .SequenceEqual(
                        assigned.Keys
                            .Select(k => (
                                k.ItemId,
                                k.ProductId))
                            .OrderBy(k => k.ItemId)))
            {
                return (null, "LICENSE_ITEMS_CONFLICT");
            }

            var replaced = await db.LicenseReplacements
                .AnyAsync(
                    x => x.OrderId == request.OrderId,
                    ct);

            if (replaced)
                return (null, "ORDER_ALREADY_COMPENSATED");

            if (request.Mode == "UNREVEALED" &&
                keys.Any(k => k.RevealedAtUtc != null))
            {
                return (null, "LICENSE_ALREADY_REVEALED");
            }

            if (request.Mode == "VERIFIED_DEFECT")
            {
                // Full refund after disclosure is limited
                // to single-license orders.
                if (keys.Count != 1 ||
                    request.DefectiveLicenseId is null ||
                    keys[0].Id != request.DefectiveLicenseId ||
                    keys[0].RevealedAtUtc is null)
                {
                    return (null, "DEFECT_REFUND_NOT_ELIGIBLE");
                }
            }
        }

        var guard = new LicenseRefundGuard
        {
            OrderId = request.OrderId,
            RequestId = request.RequestId,
            CustomerId = request.CustomerId,
            Mode = request.Mode,
            Status = "PREPARED",
            CreatedAtUtc = DateTime.UtcNow
        };

        db.LicenseRefundGuards.Add(guard);

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (Result(guard), null);
    }

    public async Task<(
        LicenseRefundResult? Result,
        string? Error)> FinalizeAsync(
        Guid orderId,
        Guid requestId,
        CancellationToken ct)
    {
        if (orderId == Guid.Empty ||
            requestId == Guid.Empty)
        {
            return (null, "INVALID_REFUND_REQUEST");
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        await LicenseOrderLock.AcquireAsync(
            db, orderId, ct);

        var guard = await db.LicenseRefundGuards
            .SingleOrDefaultAsync(
                x => x.OrderId == orderId, ct);

        if (guard is null ||
            guard.RequestId != requestId)
        {
            return (null, "REFUND_GUARD_NOT_FOUND");
        }

        if (guard.Status == "FINALIZED")
        {
            await tx.CommitAsync(ct);

            return (Result(guard), null);
        }

        if (guard.Status != "PREPARED")
            return (null, "INVALID_REFUND_GUARD_STATUS");

        var keys = await db.LicenseKeys
            .FromSqlInterpolated($"""
                SELECT *
                FROM license_keys
                WHERE "OrderId" = {orderId}
                  AND "Status" = {LicenseStatus.Assigned}
                ORDER BY "Id"
                FOR UPDATE
                """)
            .ToListAsync(ct);

        if (keys.Any(
            k => k.CustomerId != guard.CustomerId ||
                 k.OrderItemId is null))
        {
            return (null, "LICENSE_OWNER_CONFLICT");
        }

        if (guard.Mode == "OUT_OF_STOCK")
        {
            if (keys.Count != 0)
                return (null, "LICENSE_STATE_CONFLICT");
        }
        else
        {
            if (keys.Count == 0 ||
                (guard.Mode == "UNREVEALED" &&
                 keys.Any(k => k.RevealedAtUtc != null)) ||
                (guard.Mode == "VERIFIED_DEFECT" &&
                 (keys.Count != 1 ||
                  keys[0].RevealedAtUtc is null)))
            {
                return (null, "LICENSE_STATE_CONFLICT");
            }
        }

        foreach (var key in keys)
        {
            if (key.RevealedAtUtc is not null)
            {
                // Disclosed keys must never be resold.
                key.Status = LicenseStatus.Disabled;
                key.OrderItemId = null;
            }
            else
            {
                key.Status = LicenseStatus.Available;

                key.OrderId = null;
                key.OrderItemId = null;
                key.CustomerId = null;
                key.AssignedAtUtc = null;
                key.RevealedAtUtc = null;
            }
        }

        guard.Status = "FINALIZED";
        guard.FinalizedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (Result(guard), null);
    }
}
