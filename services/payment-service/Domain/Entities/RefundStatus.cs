namespace PaymentService.Domain.Entities;

public static class RefundStatus
{
    public const string Pending = "PENDING";
    public const string Processing = "PROCESSING";
    public const string Succeeded = "SUCCEEDED";
    public const string Failed = "FAILED";
    public const string Unknown = "UNKNOWN";
}
