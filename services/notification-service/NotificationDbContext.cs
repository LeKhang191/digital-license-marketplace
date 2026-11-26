using Microsoft.EntityFrameworkCore;

namespace Notification.Worker;

public sealed class NotificationMessage
{
    public Guid Id { get; set; }

    public Guid OrderId { get; set; }

    public string CustomerId { get; set; } = "";

    public string RecipientEmail { get; set; } = "";

    public string Type { get; set; } = "";

    public decimal Amount { get; set; }

    public string Status { get; set; } = "PENDING";

    public int RetryCount { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime NextRetryAtUtc { get; set; }

    public DateTime? SentAtUtc { get; set; }

    public string? LastError { get; set; }
}

public sealed class NotificationDbContext(
    DbContextOptions<NotificationDbContext> options
) : DbContext(options)
{
    public DbSet<NotificationMessage> Messages =>
        Set<NotificationMessage>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        var entity = builder.Entity<NotificationMessage>();

        entity.ToTable("notification_messages");

        entity.HasKey(x => x.Id);

        entity.HasIndex(x => new
        {
            x.OrderId,
            x.Type
        }).IsUnique();

        entity.HasIndex(x => new
        {
            x.Status,
            x.NextRetryAtUtc
        });

        entity.Property(x => x.Amount)
            .HasPrecision(12, 2);

        entity.Property(x => x.CustomerId)
            .HasMaxLength(64);

        entity.Property(x => x.RecipientEmail)
            .HasMaxLength(320);

        entity.Property(x => x.Type)
            .HasMaxLength(20);

        entity.Property(x => x.Status)
            .HasMaxLength(20);

        entity.Property(x => x.LastError)
            .HasMaxLength(100);
    }
}
