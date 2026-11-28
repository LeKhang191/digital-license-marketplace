using Microsoft.EntityFrameworkCore;
using SupportService.Domain.Entities;

namespace SupportService.Infrastructure.Persistence;

public sealed class SupportDbContext(
    DbContextOptions<SupportDbContext> options
) : DbContext(options)
{
    public DbSet<SupportTicket> SupportTickets =>
        Set<SupportTicket>();

    public DbSet<SupportMessage> SupportMessages =>
        Set<SupportMessage>();

    public DbSet<ExternalSupportLink> ExternalSupportLinks => Set<ExternalSupportLink>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        var link = modelBuilder.Entity<ExternalSupportLink>();
        link.ToTable("support_external_links");
        link.HasKey(x => x.TicketId);
        link.HasOne<SupportTicket>().WithOne().HasForeignKey<ExternalSupportLink>(x => x.TicketId)
            .OnDelete(DeleteBehavior.Restrict);
        link.HasIndex(x => x.IssueId).IsUnique();
        link.Property(x => x.Version).IsConcurrencyToken();
        link.Property(x => x.CommentsJson).HasColumnType("jsonb");

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
        ticket.Property(x => x.ResolutionOutcome).HasMaxLength(32);
        ticket.Property(x => x.RejectionReason).HasMaxLength(500);

        ticket.Property(x => x.Version).IsConcurrencyToken();

        ticket.HasIndex(x => x.CustomerId);
        ticket.HasIndex(x => new { x.AssignedRole, x.Status });

        ticket.HasIndex(x => new { x.OrderId, x.Category })
            .IsUnique()
            .HasFilter("\"Status\" IN ('OPEN', 'IN_PROGRESS')");

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

    }
}
