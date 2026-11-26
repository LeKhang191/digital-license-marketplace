namespace PaymentService.Domain.Entities;

public sealed class WalletTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid WalletId { get; set; }

    public string Type { get; set; } = "";

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public Guid ReferenceId { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;
}
