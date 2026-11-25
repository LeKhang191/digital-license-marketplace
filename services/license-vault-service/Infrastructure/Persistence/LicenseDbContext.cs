
using LicenseVault.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Persistence;

public sealed class LicenseDbContext(
    DbContextOptions<LicenseDbContext> options
) : DbContext(options)
{
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var entity = modelBuilder.Entity<LicenseKey>();

        entity.ToTable("license_keys", table =>
        {
            table.HasCheckConstraint(
                "CK_license_keys_Status",
                "\"Status\" IN ('AVAILABLE', 'ASSIGNED', 'DISABLED')"
            );
        });

        entity.HasKey(x => x.Id);

        entity.Property(x => x.ProductId)
            .IsRequired();

        entity.Property(x => x.EncryptedKey)
            .HasColumnType("text")
            .IsRequired();

        entity.Property(x => x.KeyFingerprint)
            .HasMaxLength(64)
            .IsRequired();

        entity.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        entity.Property(x => x.CustomerId)
            .HasMaxLength(255);

        entity.Property(x => x.CreatedAtUtc)
            .IsRequired();

        entity.HasIndex(x => x.KeyFingerprint)
            .IsUnique();

        entity.HasIndex(x => new
        {
            x.ProductId,
            x.Status
        });

        entity.HasIndex(x => x.OrderId);

        entity.HasIndex(x => x.CustomerId);

        entity.HasIndex(x => x.OrderItemId)
            .IsUnique()
            .HasFilter("\"OrderItemId\" IS NOT NULL");
    }
}
