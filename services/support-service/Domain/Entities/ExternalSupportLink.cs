namespace SupportService.Domain.Entities;

public sealed class ExternalSupportLink
{
    public Guid TicketId { get; set; }
    public string State { get; set; } = "PENDING";
    public string? IssueId { get; set; }
    public string? IssueKey { get; set; }
    public string? ExternalStatus { get; set; }
    public string CommentsJson { get; set; } = "[]";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? SyncedAtUtc { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
