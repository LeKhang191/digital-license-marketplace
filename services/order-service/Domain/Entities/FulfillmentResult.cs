namespace OrderService.Domain.Entities;

public sealed class FulfillmentResult
{
    public Guid OrderId { get; set; }

    public string Status { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime ProcessedAtUtc { get; set; } =
        DateTime.UtcNow;
}
