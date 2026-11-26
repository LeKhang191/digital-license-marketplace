namespace Contracts.Events;

// Payment Service -> Order Service
public sealed record PaymentSucceededEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    decimal Amount,
    string PaymentRef,
    string? TransactionNo,
    string? PayDate,
    DateTime PaidAtUtc,
    string PaymentMethod = "VNPAY");

public sealed record PaymentFailedEvent(
    Guid PaymentId,
    Guid OrderId,
    string CustomerId,
    string PaymentRef,
    string ResponseCode,
    string TransactionStatus,
    DateTime FailedAtUtc);

public sealed record PaymentRefundedEvent(
    Guid RefundId,
    Guid PaymentId,
    Guid OrderId,
    decimal Amount,
    DateTime RefundedAtUtc,
    Guid RequestId,
    string CustomerId,
    string PaymentRef);
