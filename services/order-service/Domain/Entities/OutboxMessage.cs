
namespace OrderService.Domain.Entities;

public sealed class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid AggregateId { get; set; }

    public string RoutingKey { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? PublishedAtUtc { get; set; }

    public int RetryCount { get; set; }

    public DateTime NextRetryAtUtc { get; set; } = DateTime.UtcNow;

    public string? LastError { get; set; }
}
