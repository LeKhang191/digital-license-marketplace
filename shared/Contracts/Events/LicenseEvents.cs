namespace Contracts.Events;

// Never include plaintext license keys in broker messages.
public sealed record AssignedKey(
    Guid ItemId,
    Guid ProductId,
    Guid LicenseId);

// License Vault -> Order Service
public sealed record KeyAssignedEvent(
    Guid OrderId,
    string KeycloakUserId,
    string? CustomerEmail,
    IReadOnlyList<AssignedKey> Keys);

// License Vault -> Order Service
public sealed record KeyOutOfStockEvent(
    Guid OrderId,
    Guid ProductId,
    string Reason);
