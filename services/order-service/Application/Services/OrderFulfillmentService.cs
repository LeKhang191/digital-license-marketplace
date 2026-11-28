using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

public sealed class OrderFulfillmentService(
    OrderDbContext db,
    ILogger<OrderFulfillmentService> log)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    public Task ProcessAssignedAsync(
        KeyAssignedEvent message,
        CancellationToken ct)
    {
        if (message.Keys is null ||
            message.Keys.Count == 0 ||
            message.Keys.Any(k =>
                k is null ||
                k.LicenseId == Guid.Empty ||
                k.ItemId == Guid.Empty ||
                k.ProductId == Guid.Empty))
        {
            throw new InvalidDataException(
                "Invalid assigned licenses.");
        }

        if (message.Keys
            .Select(k => k.LicenseId)
            .Distinct()
            .Count() != message.Keys.Count)
        {
            throw new InvalidDataException(
                "Duplicate license IDs.");
        }

        var normalized = message with
        {
            Keys = message.Keys
                .OrderBy(k => k.ItemId)
                .ToArray()
        };

        return ProcessAsync(
            message.OrderId,
            "ASSIGNED",
            JsonSerializer.Serialize(normalized, Json),
            order =>
            {
                if (order.KeycloakUserId !=
                    message.KeycloakUserId)
                {
                    throw new InvalidDataException(
                        "Customer ID mismatch.");
                }

                var expected = order.Items
                    .Select(i => (i.Id, i.ProductId))
                    .OrderBy(i => i.Id)
                    .ToArray();

                var actual = message.Keys
                    .Select(k => (k.ItemId, k.ProductId))
                    .OrderBy(k => k.ItemId)
                    .ToArray();

                if (!expected.SequenceEqual(actual))
                {
                    throw new InvalidDataException(
                        "Assigned items do not match the order.");
                }
            },
            ct);
    }

    public Task ProcessOutOfStockAsync(
        KeyOutOfStockEvent message,
        CancellationToken ct)
    {
        return ProcessAsync(
            message.OrderId,
            "OUT_OF_STOCK",
            JsonSerializer.Serialize(message, Json),
            order =>
            {
                if (!order.Items.Any(
                    i => i.ProductId == message.ProductId))
                {
                    throw new InvalidDataException(
                        "Product does not belong to order.");
                }
            },
            ct);
    }

    private async Task ProcessAsync(
        Guid orderId,
        string status,
        string payload,
        Action<Order> validate,
        CancellationToken ct)
    {
        if (orderId == Guid.Empty)
        {
            throw new InvalidDataException(
                "Order ID is missing.");
        }

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        // Serialize fulfillment events for the same order.
        var lockKey = $"fulfillment:{orderId:N}";

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT pg_advisory_xact_lock(
                hashtextextended({lockKey}, 0)
            )
            """,
            ct);

        var order = await db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId,
                ct);

        if (order is null)
        {
            throw new InvalidDataException(
                "Order not found.");
        }

        validate(order);

        var previous = await db.FulfillmentResults
            .AsNoTracking()
            .FirstOrDefaultAsync(
                x => x.OrderId == orderId,
                ct);

        if (previous is not null)
        {
            // JSONB may change property order, so compare
            // deserialized and normalized event payloads.
            if (previous.Status != status ||
                !EquivalentPayload(
                    status,
                    previous.Payload,
                    payload))
            {
                throw new InvalidDataException(
                    "Conflicting fulfillment result.");
            }

            await transaction.CommitAsync(ct);

            log.LogInformation(
                "Fulfillment for {OrderId} already processed",
                orderId);

            return;
        }

        if (order.Status != OrderStatus.Paid)
        {
            throw new InvalidDataException(
                "Order is not in PAID status.");
        }

        if (status == "ASSIGNED")
        {
            var updated = await db.Orders
                .Where(o =>
                    o.Id == orderId &&
                    o.Status == OrderStatus.Paid)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        o => o.Status,
                        OrderStatus.Completed),
                    ct);

            if (updated != 1)
            {
                throw new InvalidDataException(
                    "Order status changed concurrently.");
            }
        }
        else
        {
            // Lock and verify PAID status before recording
            // a terminal allocation failure.
            var locked = await db.Orders
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM orders
                    WHERE "Id" = {orderId}
                      AND "Status" = {OrderStatus.Paid}
                    FOR UPDATE
                    """)
                .AsNoTracking()
                .ToListAsync(ct);

            if (locked.Count != 1)
            {
                throw new InvalidDataException(
                    "Order status changed concurrently.");
            }
        }

        if (status == "ASSIGNED")
        {
            var now = DateTime.UtcNow;
            order.Status = OrderStatus.Completed;

            db.OutboxMessages.Add(new OutboxMessage
            {
                AggregateId = order.Id,
                RoutingKey = EventRoutes.OrderCompleted,
                Payload = JsonSerializer.Serialize(
                    new OrderCompletedEvent(
                        order.Id,
                        order.KeycloakUserId,
                        order.CustomerEmail,
                        order.TotalAmount,
                        order.CustomerEmailVerified),
                    Json),
                CreatedAtUtc = now,
                NextRetryAtUtc = now
            });

            await PartnerWebhookHelper.TryEnqueueAsync(
                db, order, PartnerWebhookEvents.OrderCompleted, ct);
        }

        db.FulfillmentResults.Add(
            new FulfillmentResult
            {
                OrderId = orderId,
                Status = status,
                Payload = payload,
                ProcessedAtUtc = DateTime.UtcNow
            });

        // Automatic full refund for terminal stock failure.
        // Synthetic allocation-only test orders have no payment reference.
        if (status == "OUT_OF_STOCK" &&
            order.PaidAtUtc is not null &&
            order.TotalAmount > 0 &&
            !string.IsNullOrWhiteSpace(order.PaymentRef))
        {
            var exists = await db.RefundOperations
                .AnyAsync(
                    x => x.OrderId == order.Id, ct);

            if (!exists)
            {
                var now = DateTime.UtcNow;

                db.RefundOperations.Add(new RefundOperation
                {
                    OrderId = order.Id,
                    RequestId = Guid.NewGuid(),
                    CustomerId = order.KeycloakUserId,
                    PaymentRef = order.PaymentRef!,
                    Mode = "OUT_OF_STOCK",
                    Reason = "Automatic refund: insufficient license stock",
                    RequestedBy = "SYSTEM",
                    Status = "REQUESTED",
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now,
                    NextRetryAtUtc = now
                });
            }
        }

        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        log.LogInformation(
            "Order {OrderId} fulfillment recorded: {Status}",
            orderId,
            status);
    }

    private static bool EquivalentPayload(
        string status,
        string left,
        string right)
    {
        if (status == "ASSIGNED")
        {
            var a = JsonSerializer.Deserialize<KeyAssignedEvent>(
                left, Json);

            var b = JsonSerializer.Deserialize<KeyAssignedEvent>(
                right, Json);

            if (a is null || b is null)
                return false;

            return a.OrderId == b.OrderId &&
                a.KeycloakUserId == b.KeycloakUserId &&
                a.CustomerEmail == b.CustomerEmail &&
                a.Keys.OrderBy(k => k.ItemId)
                    .SequenceEqual(
                        b.Keys.OrderBy(k => k.ItemId));
        }

        return
            JsonSerializer.Deserialize<KeyOutOfStockEvent>(
                left, Json)
            ==
            JsonSerializer.Deserialize<KeyOutOfStockEvent>(
                right, Json);
    }
}
