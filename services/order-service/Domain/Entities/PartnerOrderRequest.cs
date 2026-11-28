namespace OrderService.Domain.Entities;

public sealed class PartnerOrderRequest
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PartnerId { get; set; } = "";

    public string IdempotencyKey { get; set; } = "";

    public string RequestHash { get; set; } = "";

    public string? ExternalReference { get; set; }

    public Guid OrderId { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
