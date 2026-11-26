
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderDbContext(
    DbContextOptions<OrderDbContext> options
) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();

    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    public DbSet<FulfillmentResult> FulfillmentResults =>
        Set<FulfillmentResult>();

    public DbSet<SupportTicket> SupportTickets =>
        Set<SupportTicket>();

    public DbSet<SupportMessage> SupportMessages =>
        Set<SupportMessage>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        var ticket = modelBuilder.Entity<SupportTicket>();

        ticket.ToTable("support_tickets");
        ticket.HasKey(x => x.Id);

        ticket.Property(x => x.CustomerId).HasMaxLength(64);
        ticket.Property(x => x.Category).HasMaxLength(32);
        ticket.Property(x => x.Subject).HasMaxLength(150);
        ticket.Property(x => x.Description).HasMaxLength(3000);
        ticket.Property(x => x.Status).HasMaxLength(20);
        ticket.Property(x => x.AssignedRole).HasMaxLength(32);
        ticket.Property(x => x.HandledBy).HasMaxLength(64);

        ticket.Property(x => x.Version).IsConcurrencyToken();

        ticket.HasIndex(x => x.CustomerId);
        ticket.HasIndex(x => new { x.AssignedRole, x.Status });

        ticket.HasIndex(x => new { x.OrderId, x.Category })
            .IsUnique()
            .HasFilter("\"Status\" IN ('OPEN', 'IN_PROGRESS')");

        ticket.HasOne<Order>()
            .WithMany()
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        var message = modelBuilder.Entity<SupportMessage>();

        message.ToTable("support_messages");
        message.HasKey(x => x.Id);

        message.Property(x => x.SenderId).HasMaxLength(64);
        message.Property(x => x.SenderRole).HasMaxLength(32);
        message.Property(x => x.Content).HasMaxLength(3000);

        message.HasOne<SupportTicket>()
            .WithMany(x => x.Messages)
            .HasForeignKey(x => x.TicketId)
            .OnDelete(DeleteBehavior.Restrict);

        message.HasIndex(x => new { x.TicketId, x.CreatedAtUtc });

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

        order.ToTable("orders");
        order.HasKey(o => o.Id);

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
