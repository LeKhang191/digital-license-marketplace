using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;

namespace OrderService.Infrastructure.Persistence;

public sealed class OrderDbContext(
    DbContextOptions<OrderDbContext> options
) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var order = modelBuilder.Entity<Order>();
        order.ToTable("orders");
        order.HasKey(o => o.Id);
        order.HasIndex(o => o.KeycloakUserId);
        order.HasIndex(o => o.PaymentRef);
        order.Property(o => o.KeycloakUserId).HasMaxLength(64).IsRequired();
        order.Property(o => o.CustomerEmail).HasMaxLength(256);
        order.Property(o => o.Status).HasMaxLength(20).IsRequired();
        order.Property(o => o.TotalAmount).HasPrecision(12, 2);
        order.Property(o => o.PaymentRef).HasMaxLength(64);
        order.Property(o => o.VnpTransactionNo).HasMaxLength(32);
        order.Property(o => o.VnpPayDate).HasMaxLength(14);

        var item = modelBuilder.Entity<OrderItem>();
        item.ToTable("order_items");
        item.HasKey(i => i.Id);
        item.Property(i => i.Price).HasPrecision(12, 2);
        item.HasOne(i => i.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}