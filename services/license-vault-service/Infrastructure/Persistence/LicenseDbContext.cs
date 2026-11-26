
using LicenseVault.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Persistence;

public sealed class LicenseDbContext(
    DbContextOptions<LicenseDbContext> options
) : DbContext(options)
{
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();

    public DbSet<AllocationResult> AllocationResults =>
        Set<AllocationResult>();

    public DbSet<LicenseReplacement> LicenseReplacements =>
        Set<LicenseReplacement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var replacement = modelBuilder.Entity<LicenseReplacement>();

        replacement.ToTable("license_replacements");
        replacement.HasKey(x => x.TicketId);

        replacement.Property(x => x.CustomerId)
            .HasMaxLength(255).IsRequired();

        replacement.Property(x => x.HandledBy)
            .HasMaxLength(255).IsRequired();

        replacement.HasIndex(x => x.OldLicenseId)
            .IsUnique();

        replacement.HasIndex(x => x.NewLicenseId)
            .IsUnique();

        replacement.HasIndex(x => x.OrderId);

        replacement.HasOne<LicenseKey>()
            .WithMany()
            .HasForeignKey(x => x.OldLicenseId)
            .OnDelete(DeleteBehavior.Restrict);

        replacement.HasOne<LicenseKey>()
            .WithMany()
            .HasForeignKey(x => x.NewLicenseId)
            .OnDelete(DeleteBehavior.Restrict);



        var allocation = modelBuilder.Entity<AllocationResult>();

        allocation.ToTable("allocation_results");

        allocation.HasKey(x => x.OrderId);

        allocation.Property(x => x.CustomerId)
            .HasMaxLength(255)
            .IsRequired();

        allocation.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        allocation.Property(x => x.RequestFingerprint)
            .HasMaxLength(64)
            .IsRequired();

        allocation.Property(x => x.RoutingKey)
            .HasMaxLength(100)
            .IsRequired();

        allocation.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        allocation.Property(x => x.LastError)
            .HasMaxLength(500);

        allocation.HasIndex(x => new
        {
            x.PublishedAtUtc,
            x.NextRetryAtUtc
        });


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
