namespace OrderService.Domain.Entities;

public static class OrderStatus
{
    public const string Pending = "PENDING";
    public const string Paid = "PAID";
    public const string Completed = "COMPLETED";
    public const string Refunding = "REFUNDING";
    public const string Refunded = "REFUNDED";
    public const string Canceled = "CANCELED";
}
