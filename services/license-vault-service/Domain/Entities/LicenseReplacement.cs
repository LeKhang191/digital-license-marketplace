namespace LicenseVault.Api.Domain.Entities;

public sealed class LicenseReplacement
{
    public Guid TicketId { get; set; }
    public Guid OrderId { get; set; }
    public Guid OrderItemId { get; set; }
    public Guid ProductId { get; set; }

    public Guid OldLicenseId { get; set; }
    public Guid NewLicenseId { get; set; }

    public string CustomerId { get; set; } = "";
    public string HandledBy { get; set; } = "";

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
