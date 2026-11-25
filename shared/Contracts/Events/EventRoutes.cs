namespace Contracts.Events;

// Tên exchange và routing key dùng chung cho mọi service
public static class EventRoutes
{
    public const string Exchange = "marketplace.events";

    public const string OrderPaid = "order.paid";
    public const string LicenseDelivered = "license.delivered.v1";
    public const string OrderDeliveryQueue = "order.license-delivered.v1";
    public const string KeyAssigned = "key.assigned";
    public const string KeyOutOfStock = "key.out_of_stock";
    public const string OrderRefunded = "order.refunded";
}
