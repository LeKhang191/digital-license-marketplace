
using LicenseVault.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Persistence;

public sealed class LicenseDbContext(
    DbContextOptions<LicenseDbContext> options
) : DbContext(options)
{
    public DbSet<LicenseKey> LicenseKeys => Set<LicenseKey>();

    public DbSet<LicenseRefundGuard> LicenseRefundGuards =>
        Set<LicenseRefundGuard>();


    public DbSet<AllocationResult> AllocationResults =>
        Set<AllocationResult>();

    public DbSet<LicenseReplacement> LicenseReplacements =>
        Set<LicenseReplacement>();

    public DbSet<KinguinMockOrder> KinguinMockOrders => Set<KinguinMockOrder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        var mock = modelBuilder.Entity<KinguinMockOrder>();
        mock.ToTable("kinguin_mock_orders");
        mock.HasKey(x => x.Id);
        mock.Property(x => x.Id).ValueGeneratedNever();
        mock.Property(x => x.SupplierProductId).HasMaxLength(128).IsRequired();
        mock.Property(x => x.OwnerId).HasMaxLength(255).IsRequired();
        mock.Property(x => x.TotalCost).HasPrecision(18, 2);
        mock.Property(x => x.EncryptedKeys).IsRequired();
        mock.HasIndex(x => x.SupplierProductId);
        mock.Property(x => x.Status).HasMaxLength(32).IsRequired();
        mock.HasIndex(x => new { x.Status, x.NextPublishAtUtc });

        var guard = modelBuilder.Entity<LicenseRefundGuard>();

        guard.ToTable("license_refund_guards", table =>
        {
            table.HasCheckConstraint(
                "CK_license_refund_guard_status",
                "\"Status\" IN ('PREPARED', 'FINALIZED')");

            table.HasCheckConstraint(
                "CK_license_refund_guard_mode",
                "\"Mode\" IN ('UNREVEALED', 'OUT_OF_STOCK', 'VERIFIED_DEFECT')");
        });

        guard.HasKey(x => x.OrderId);

        guard.HasIndex(x => x.RequestId)
            .IsUnique();

        guard.Property(x => x.CustomerId)
            .HasMaxLength(255)
            .IsRequired();

        guard.Property(x => x.Mode)
            .HasMaxLength(32)
            .IsRequired();

        guard.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();


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


        var supplier = modelBuilder.Entity<Supplier>();
        supplier.ToTable("suppliers");
        supplier.HasKey(x => x.Id);
        supplier.Property(x => x.Name).HasMaxLength(160).IsRequired();
        supplier.Property(x => x.NormalizedName).HasMaxLength(160).IsRequired();
        supplier.HasIndex(x => x.NormalizedName).IsUnique();
        supplier.Property(x => x.Website).HasMaxLength(500);
        supplier.Property(x => x.Email).HasMaxLength(255);
        supplier.Property(x => x.Phone).HasMaxLength(80);
        supplier.Property(x => x.Notes).HasMaxLength(2000);
        var batch = modelBuilder.Entity<LicenseImportBatch>();
        batch.HasOne<Supplier>().WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
        batch.ToTable("license_import_batches");
        batch.HasKey(x => x.Id);
        batch.Property(x => x.SupplierName).HasMaxLength(160).IsRequired();
        batch.Property(x => x.Reference).HasMaxLength(120);
        batch.Property(x => x.UnitCostVnd).HasPrecision(18, 2);
        batch.Property(x => x.Method).HasMaxLength(16).IsRequired();
        batch.Property(x => x.ImportedBy).HasMaxLength(255).IsRequired();
        batch.HasIndex(x => x.CreatedAtUtc);
        var entity = modelBuilder.Entity<LicenseKey>();
        entity.HasOne<LicenseImportBatch>().WithMany()
            .HasForeignKey(x => x.ImportBatchId).OnDelete(DeleteBehavior.Restrict);

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
