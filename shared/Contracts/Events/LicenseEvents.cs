namespace Contracts.Events;

public sealed record AssignedKey(Guid ItemId, Guid ProductId, string Key);

// License Vault -> RabbitMQ: đã gán Key cho đơn (Notification gửi email)
// Lưu ý: Key ở dạng đã giải mã, chỉ đi qua broker nội bộ, không expose ra ngoài.
public sealed record KeyAssignedEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    IReadOnlyList<AssignedKey> Keys);

// License Vault -> RabbitMQ: hết Key, kích hoạt hoàn tiền (Saga bù trừ)
public sealed record KeyOutOfStockEvent(
    Guid OrderId,
    Guid ProductId,
    string Reason);
