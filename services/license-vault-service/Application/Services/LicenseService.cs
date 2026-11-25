
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
        CancellationToken ct)
    {
        int imported = 0;
        int duplicates = 0;
        int invalid = 0;

        var seen = new HashSet<string>(
            StringComparer.Ordinal);

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

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
                        "CreatedAtUtc"
                    )
                    VALUES
                    (
                        {id},
                        {productId},
                        {encrypted},
                        {fingerprint},
                        {LicenseStatus.Available},
                        {now}
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

        await transaction.CommitAsync(ct);

        return new ImportLicensesResponse(
            productId,
            keys.Count,
            imported,
            duplicates,
            invalid
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
