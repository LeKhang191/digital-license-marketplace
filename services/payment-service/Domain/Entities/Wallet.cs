namespace PaymentService.Domain.Entities;

public sealed class Wallet
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string CustomerId { get; set; } = "";

    public decimal Balance { get; set; }

    public DateTime CreatedAtUtc { get; set; } =
        DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } =
        DateTime.UtcNow;
}
