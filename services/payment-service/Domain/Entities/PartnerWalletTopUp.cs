namespace PaymentService.Domain.Entities;

public sealed class PartnerWalletTopUp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PartnerId { get; set; } = "";

    public string IdempotencyKey { get; set; } = "";

    public decimal Amount { get; set; }

    public decimal BalanceAfter { get; set; }

    public string? Note { get; set; }

    public string RequestedBy { get; set; } = "";

    public Guid WalletTransactionId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
