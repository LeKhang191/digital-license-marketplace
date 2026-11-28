namespace Contracts.Events;

// Tên exchange và routing key dùng chung cho mọi service
public static class EventRoutes
{
    public const string Exchange = "marketplace.events";

    public const string PaymentSucceeded = "payment.succeeded";
    public const string PaymentFailed = "payment.failed";
    public const string PaymentRefunded = "payment.refunded";

    public const string OrderPaid = "order.paid";
    public const string KeyAssigned = "key.assigned";
    public const string KeyOutOfStock = "key.out_of_stock";
    public const string OrderRefunded = "order.refunded";
    public const string OrderCompleted = "order.completed";
    public const string RefundRejected = "order.refund_rejected";
}
