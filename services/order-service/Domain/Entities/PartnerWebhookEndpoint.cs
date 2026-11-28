namespace OrderService.Domain.Entities;

public sealed class PartnerWebhookEndpoint
{
    public string PartnerId { get; set; } = "";

    public string CallbackUrl { get; set; } = "";

    public string SigningSecret { get; set; } = "";

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
