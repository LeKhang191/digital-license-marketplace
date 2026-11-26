namespace PaymentService.Domain.Entities;

public static class PaymentStatus
{
    public const string Pending = "PENDING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Refunding = "REFUNDING";
    public const string Refunded = "REFUNDED";
}
