namespace Contracts.Events;

public sealed record OrderItemRef(Guid ItemId, Guid ProductId);

// Order Service -> RabbitMQ: thanh toán thành công (sau khi xác thực IPN)
public sealed record OrderPaidEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    decimal Amount,
    IReadOnlyList<OrderItemRef> Items,
    DateTime PaidAtUtc);

// Order Service -> RabbitMQ: đã hoàn tiền xong
public sealed record OrderRefundedEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    decimal Amount,
    bool CustomerEmailVerified = false);

public sealed record OrderCompletedEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    decimal Amount,
    bool CustomerEmailVerified);

public sealed record OrderRefundRejectedEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    decimal Amount,
    bool CustomerEmailVerified,
    string Reason);
