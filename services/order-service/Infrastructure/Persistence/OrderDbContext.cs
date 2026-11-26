
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderDbContext(
    DbContextOptions<OrderDbContext> options
) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<RefundOperation> RefundOperations =>
        Set<RefundOperation>();


    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    public DbSet<FulfillmentResult> FulfillmentResults =>
        Set<FulfillmentResult>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        var refund = modelBuilder.Entity<RefundOperation>();

        refund.ToTable("refund_operations", table =>
        {
            table.HasCheckConstraint(
                "CK_refund_operations_status",
                "\"Status\" IN ('REQUESTED', 'PREPARED', 'CREDITED', 'COMPLETED', 'REJECTED')");

            table.HasCheckConstraint(
                "CK_refund_operations_mode",
                "\"Mode\" IN ('UNREVEALED', 'OUT_OF_STOCK', 'VERIFIED_DEFECT')");
        });

        refund.HasKey(x => x.OrderId);

        refund.HasIndex(x => x.RequestId).IsUnique();

        refund.HasIndex(x => new
        {
            x.Status,
            x.NextRetryAtUtc
        });

        refund.Property(x => x.CustomerId)
            .HasMaxLength(64).IsRequired();

        refund.Property(x => x.PaymentRef)
            .HasMaxLength(64).IsRequired();

        refund.Property(x => x.RequestedBy)
            .HasMaxLength(64).IsRequired();

        refund.Property(x => x.Reason)
            .HasMaxLength(500).IsRequired();

        refund.Property(x => x.Mode)
            .HasMaxLength(32).IsRequired();

        refund.Property(x => x.Status)
            .HasMaxLength(20).IsRequired();

        refund.Property(x => x.LastError)
            .HasMaxLength(500);

        refund.HasOne<Order>()
            .WithOne()
            .HasForeignKey<RefundOperation>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        var fulfillment =
            modelBuilder.Entity<FulfillmentResult>();

        fulfillment.ToTable("fulfillment_results");

        fulfillment.HasKey(x => x.OrderId);

        fulfillment.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        fulfillment.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        fulfillment.HasIndex(x => x.Status);

        fulfillment.HasOne<Order>()
            .WithOne()
            .HasForeignKey<FulfillmentResult>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        var order = modelBuilder.Entity<Order>();

        order.ToTable("orders", table =>
        {
            table.HasCheckConstraint(
                "CK_orders_payment_method",
                "\"PaymentMethod\" IN ('VNPAY', 'WALLET')");
        });
        order.HasKey(o => o.Id);

        order.Property(x => x.PaymentMethod)
            .HasMaxLength(20)
            .HasDefaultValue("VNPAY")
            .IsRequired();

        order.HasIndex(o => o.KeycloakUserId);
        order.HasIndex(o => o.PaymentRef);

        order.Property(o => o.KeycloakUserId)
            .HasMaxLength(64)
            .IsRequired();

        order.Property(o => o.CustomerEmail)
            .HasMaxLength(256);

        order.Property(o => o.Status)
            .HasMaxLength(20)
            .IsRequired();

        order.Property(o => o.TotalAmount)
            .HasPrecision(12, 2);

        order.Property(o => o.PaymentRef)
            .HasMaxLength(64);

        order.Property(o => o.VnpTransactionNo)
            .HasMaxLength(32);

        order.Property(o => o.VnpPayDate)
            .HasMaxLength(14);

        var item = modelBuilder.Entity<OrderItem>();

        item.ToTable("order_items");
        item.HasKey(i => i.Id);

        item.Property(i => i.Price)
            .HasPrecision(12, 2);

        item.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        // Transactional Outbox
        var outbox = modelBuilder.Entity<OutboxMessage>();

        outbox.ToTable("outbox_messages");

        outbox.HasKey(x => x.Id);

        outbox.Property(x => x.RoutingKey)
            .HasMaxLength(100)
            .IsRequired();

        outbox.Property(x => x.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        outbox.Property(x => x.LastError)
            .HasMaxLength(500);

        outbox.HasIndex(x => new
        {
            x.AggregateId,
            x.RoutingKey
        }).IsUnique();

        outbox.HasIndex(x => new
        {
            x.PublishedAtUtc,
            x.NextRetryAtUtc
        });
    }
}
