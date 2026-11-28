
using LicenseVault.Api.Application.DTOs;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Application.Services;

public sealed class LicenseService(
    LicenseDbContext db,
    ILicenseEncryption encryption
) : ILicenseService
{
    public async Task<ImportLicensesResponse> ImportAsync(
        Guid productId,
        IReadOnlyList<string> keys,
        CancellationToken ct,
        LicenseImportBatch batch)
    {
        int imported = 0;
        int duplicates = 0;
        int invalid = 0;

        var seen = new HashSet<string>(
            StringComparer.Ordinal);

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        if (batch.SupplierId is Guid supplierId)
        {
            // Lock the supplier until import commits, so deactivation cannot race this import.
            var rows = await db.Set<Supplier>().FromSqlInterpolated($"SELECT * FROM suppliers WHERE \"Id\" = {supplierId} FOR UPDATE").AsNoTracking().ToListAsync(ct);
            var supplier = rows.SingleOrDefault();
            if (supplier == null || !supplier.IsActive) throw new SupplierUnavailableException();
            batch.SupplierName = supplier.Name;
        }
        db.Add(batch);
        await db.SaveChangesAsync(ct);

        foreach (var raw in keys)
        {
            var key = raw?.Trim();

            if (string.IsNullOrWhiteSpace(key) ||
                key.Length < 5 ||
                key.Length > 512 ||
                key.Any(char.IsControl))
            {
                invalid++;
                continue;
            }

            var fingerprint =
                encryption.Fingerprint(key);

            if (!seen.Add(fingerprint))
            {
                duplicates++;
                continue;
            }

            var encrypted =
                encryption.Encrypt(key);

            var id = Guid.NewGuid();
            var now = DateTime.UtcNow;

            var affected =
                await db.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO license_keys
                    (
                        "Id",
                        "ProductId",
                        "EncryptedKey",
                        "KeyFingerprint",
                        "Status",
                        "CreatedAtUtc",
                        "ImportBatchId"
                    )
                    VALUES
                    (
                        {id},
                        {productId},
                        {encrypted},
                        {fingerprint},
                        {LicenseStatus.Available},
                        {now},
                        {batch.Id}
                    )
                    ON CONFLICT ("KeyFingerprint")
                    DO NOTHING
                    """,
                    ct);

            if (affected == 1)
                imported++;
            else
                duplicates++;
        }

        batch.Total = keys.Count;
        batch.Imported = imported;
        batch.Duplicates = duplicates;
        batch.Invalid = invalid;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return new ImportLicensesResponse(
            productId,
            keys.Count,
            imported,
            duplicates,
            invalid,
            batch.Id
        );
    }

    public async Task<
        IReadOnlyList<LicenseInventoryResponse>>
        GetInventoryAsync(CancellationToken ct)
    {
        return await db.LicenseKeys
            .AsNoTracking()
            .GroupBy(x => x.ProductId)
            .Select(g => new LicenseInventoryResponse(
                g.Key,
                g.Count(),
                g.Count(x =>
                    x.Status == LicenseStatus.Available),
                g.Count(x =>
                    x.Status == LicenseStatus.Assigned),
                g.Count(x =>
                    x.Status == LicenseStatus.Disabled)
            ))
            .ToListAsync(ct);
    }

    public async Task<
        IReadOnlyList<LicenseKeyResponse>>
        GetProductLicensesAsync(
            Guid productId,
            CancellationToken ct)
    {
        return await db.LicenseKeys
            .AsNoTracking()
            .Where(x => x.ProductId == productId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .Select(x => new LicenseKeyResponse(
                x.Id,
                x.ProductId,
                x.Status,
                x.CreatedAtUtc,
                x.AssignedAtUtc
            ))
            .Take(1000)
            .ToListAsync(ct);
    }

    public async Task<bool> DisableAsync(
        Guid id,
        CancellationToken ct)
    {
        var affected = await db.LicenseKeys
            .Where(x =>
                x.Id == id &&
                x.Status == LicenseStatus.Available)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    x => x.Status,
                    LicenseStatus.Disabled),
                ct);

        return affected == 1;
    }
}
