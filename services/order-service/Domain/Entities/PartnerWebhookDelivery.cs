namespace OrderService.Domain.Entities;

public sealed class PartnerWebhookDelivery
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string PartnerId { get; set; } = "";

    public Guid OrderId { get; set; }

    public string EventType { get; set; } = "";

    public string Payload { get; set; } = "{}";

    public string Status { get; set; } = "PENDING";

    public int AttemptCount { get; set; }

    public int? LastStatusCode { get; set; }

    public string? LastError { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime NextRetryAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? DeliveredAtUtc { get; set; }
}
