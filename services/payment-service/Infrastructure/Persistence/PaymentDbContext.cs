using Microsoft.EntityFrameworkCore;
using PaymentService.Domain.Entities;

namespace PaymentService.Infrastructure.Persistence;

public sealed class PaymentDbContext(
    DbContextOptions<PaymentDbContext> options
) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Refund> Refunds => Set<Refund>();

    public DbSet<Wallet> Wallets =>
        Set<Wallet>();

    public DbSet<WalletTransaction> WalletTransactions =>
        Set<WalletTransaction>();


    public DbSet<OutboxMessage> OutboxMessages =>
        Set<OutboxMessage>();

    protected override void OnModelCreating(
        ModelBuilder modelBuilder)
    {
        var payment = modelBuilder.Entity<Payment>();

        payment.ToTable("payments", table =>
        {
            table.HasCheckConstraint(
                "CK_payments_amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_payments_status",
                "\"Status\" IN ('PENDING', 'SUCCEEDED', 'FAILED', 'REFUNDING', 'REFUNDED')");
        });

        payment.ToTable("payments", table =>
        {
            table.HasCheckConstraint(
                "CK_payments_payment_method",
                "\"PaymentMethod\" IN ('VNPAY', 'WALLET')");
        });

        payment.HasKey(x => x.Id);

        payment.Property(x => x.PaymentMethod)
            .HasMaxLength(20)
            .HasDefaultValue("VNPAY")
            .IsRequired();

        payment.HasIndex(x => x.OrderId)
            .IsUnique();

        payment.HasIndex(x => x.PaymentRef)
            .IsUnique();

        payment.HasIndex(x => x.VnpTransactionNo)
            .IsUnique()
            .HasFilter("\"VnpTransactionNo\" IS NOT NULL");

        payment.HasIndex(x => x.CustomerId);

        payment.HasIndex(x => x.Status);

        payment.Property(x => x.CustomerId)
            .HasMaxLength(64)
            .IsRequired();

        payment.Property(x => x.PaymentRef)
            .HasMaxLength(64)
            .IsRequired();

        payment.Property(x => x.Amount)
            .HasPrecision(12, 2);

        payment.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        payment.Property(x => x.VnpTransactionNo)
            .HasMaxLength(32);

        payment.Property(x => x.VnpPayDate)
            .HasMaxLength(14);

        var refund = modelBuilder.Entity<Refund>();

        refund.ToTable("refunds", table =>
        {
            table.HasCheckConstraint(
                "CK_refunds_amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_refunds_status",
                "\"Status\" IN ('PENDING', 'PROCESSING', 'SUCCEEDED', 'FAILED', 'UNKNOWN')");
        });

        refund.HasKey(x => x.Id);

        refund.HasIndex(x => x.RequestId)
            .IsUnique();

        refund.HasIndex(x => x.PaymentId);

        refund.HasIndex(x => x.PaymentId)
            .IsUnique()
            .HasFilter(
                "\"Status\" IN ('PENDING', 'PROCESSING', 'UNKNOWN', 'SUCCEEDED')");

        refund.Property(x => x.Amount)
            .HasPrecision(12, 2);

        refund.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired();

        refund.Property(x => x.RequestedBy)
            .HasMaxLength(64)
            .IsRequired();

        refund.Property(x => x.Status)
            .HasMaxLength(20)
            .IsRequired();

        refund.Property(x => x.ProviderRefundRef)
            .HasMaxLength(64);

        refund.HasOne<Payment>()
            .WithMany()
            .HasForeignKey(x => x.PaymentId)
            .OnDelete(DeleteBehavior.Restrict);


        var wallet = modelBuilder.Entity<Wallet>();

        wallet.ToTable("wallets", table =>
        {
            table.HasCheckConstraint(
                "CK_wallets_balance",
                "\"Balance\" >= 0");
        });

        wallet.HasKey(x => x.Id);

        wallet.Property(x => x.CustomerId)
            .HasMaxLength(64)
            .IsRequired();

        wallet.HasIndex(x => x.CustomerId)
            .IsUnique();

        wallet.Property(x => x.Balance)
            .HasPrecision(18, 2);

        var ledger =
            modelBuilder.Entity<WalletTransaction>();

        ledger.ToTable("wallet_transactions", table =>
        {
            table.HasCheckConstraint(
                "CK_wallet_transactions_amount",
                "\"Amount\" > 0");

            table.HasCheckConstraint(
                "CK_wallet_transactions_balance",
                "\"BalanceAfter\" >= 0");

            table.HasCheckConstraint(
                "CK_wallet_transactions_type",
                "\"Type\" IN ('REFUND', 'PURCHASE')");
        });

        ledger.HasKey(x => x.Id);

        ledger.Property(x => x.Type)
            .HasMaxLength(20)
            .IsRequired();

        ledger.Property(x => x.Amount)
            .HasPrecision(18, 2);

        ledger.Property(x => x.BalanceAfter)
            .HasPrecision(18, 2);

        ledger.HasIndex(x => new
        {
            x.Type,
            x.ReferenceId
        }).IsUnique();

        ledger.HasIndex(x => new
        {
            x.WalletId,
            x.CreatedAtUtc
        });

        ledger.HasOne<Wallet>()
            .WithMany()
            .HasForeignKey(x => x.WalletId)
            .OnDelete(DeleteBehavior.Restrict);

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
