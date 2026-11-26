namespace SupportService.Domain.Entities;

public sealed class SupportTicket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrderId { get; set; }
    public Guid? LicenseId { get; set; }

    public string CustomerId { get; set; } = "";
    public string Category { get; set; } = "";
    public string Subject { get; set; } = "";
    public string Description { get; set; } = "";

    public string Status { get; set; } = "OPEN";
    public string AssignedRole { get; set; } = "";
    public string? HandledBy { get; set; }

    public Guid Version { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public List<SupportMessage> Messages { get; set; } = new();
}

public sealed class SupportMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }

    public string SenderId { get; set; } = "";
    public string SenderRole { get; set; } = "";
    public string Content { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
