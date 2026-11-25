namespace Contracts.Events;

// Only identifiers; never include activation codes in broker messages.
public sealed record LicenseDeliveredEvent(Guid OrderId, string KeycloakUserId, IReadOnlyList<OrderItemRef> Items);
public sealed record PurchasedLicense(Guid ItemId, Guid ProductId, string Key);
