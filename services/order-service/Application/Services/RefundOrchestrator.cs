using Contracts.Events;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;
using OrderService.Infrastructure.Refund;

namespace OrderService.Application.Services;

public sealed class RefundOrchestrator(
    OrderDbContext db,
    RefundClient client,
    ILogger<RefundOrchestrator> log)
{
    private static readonly HashSet<string> Rejections = new()
    {
        "LICENSE_ALREADY_REVEALED",
        "ORDER_ALREADY_COMPENSATED",
        "DEFECT_REFUND_NOT_ELIGIBLE",
        "REFUND_NOT_ELIGIBLE"
    };

    private static void Retry(
        RefundOperation operation,
        string? error)
    {
        operation.RetryCount++;

        var delay = Math.Min(
            300,
            Math.Pow(
                2,
                Math.Min(operation.RetryCount, 8)));

        operation.NextRetryAtUtc =
            DateTime.UtcNow.AddSeconds(delay);

        var message = error ?? "UNKNOWN_REFUND_ERROR";

        operation.LastError =
            message[..Math.Min(message.Length, 500)];
    }

    private static void Advance(
        RefundOperation operation,
        string status)
    {
        operation.Status = status;
        operation.RetryCount = 0;
        operation.LastError = null;
        operation.NextRetryAtUtc = DateTime.UtcNow;
    }

    public async Task<bool> ProcessNextAsync(
        CancellationToken ct)
    {
        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        var operations = await db.RefundOperations
            .FromSqlInterpolated($"""
                SELECT *
                FROM refund_operations
                WHERE "Status" IN (
                    'REQUESTED',
                    'PREPARED',
                    'CREDITED'
                )
                  AND "NextRetryAtUtc" <= {now}
                ORDER BY "NextRetryAtUtc", "CreatedAtUtc"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var operation = operations.SingleOrDefault();

        if (operation is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var orders = await db.Orders
            .FromSqlInterpolated($"""
                SELECT *
                FROM orders
                WHERE "Id" = {operation.OrderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var order = orders.SingleOrDefault();

        if (order is null ||
            order.KeycloakUserId != operation.CustomerId ||
            order.PaymentRef != operation.PaymentRef ||
            order.PaidAtUtc is null ||
            order.TotalAmount <= 0)
        {
            Retry(operation, "ORDER_DATA_CONFLICT");
        }
        else
        {
            switch (operation.Status)
            {
                case "REQUESTED":
                {
                    var fulfillment = await db.FulfillmentResults
                        .AsNoTracking()
                        .SingleOrDefaultAsync(
                            x => x.OrderId == order.Id, ct);

                    var eligible = operation.Mode switch
                    {
                        "OUT_OF_STOCK" =>
                            order.Status == OrderStatus.Paid &&
                            fulfillment?.Status == "OUT_OF_STOCK",

                        "UNREVEALED" or "VERIFIED_DEFECT" =>
                            order.Status == OrderStatus.Completed &&
                            fulfillment?.Status == "ASSIGNED",

                        _ => false
                    };

                    if (!eligible)
                    {
                        Retry(operation, "ORDER_NOT_REFUNDABLE");
                        break;
                    }

                    var (result, error) =
                        await client.PrepareAsync(operation, ct);

                    if (result is null)
                    {
                        if (error is not null &&
                            Rejections.Contains(error))
                        {
                            Advance(operation, "REJECTED");
                            operation.LastError = error;
                        }
                        else
                        {
                            Retry(operation, error);
                        }

                        break;
                    }

                    if (result.OrderId != operation.OrderId ||
                        result.RequestId != operation.RequestId ||
                        result.Status is not (
                            "PREPARED" or "FINALIZED"))
                    {
                        Retry(operation, "INVALID_GUARD_RESPONSE");
                        break;
                    }

                    order.Status = OrderStatus.Refunding;

                    Advance(operation, "PREPARED");

                    break;
                }

                case "PREPARED":
                {
                    if (order.Status != OrderStatus.Refunding)
                    {
                        Retry(operation, "ORDER_STATE_CONFLICT");
                        break;
                    }

                    var (result, error) =
                        await client.CreditAsync(operation, ct);

                    if (result is null)
                    {
                        Retry(operation, error);
                        break;
                    }

                    if (result.OrderId != operation.OrderId ||
                        result.RequestId != operation.RequestId ||
                        result.RefundId == Guid.Empty ||
                        result.PaymentId == Guid.Empty ||
                        result.Amount != order.TotalAmount ||
                        result.CompletedAtUtc is null ||
                        result.Status != "SUCCEEDED")
                    {
                        Retry(operation, "INVALID_REFUND_RESPONSE");
                        break;
                    }

                    operation.RefundId = result.RefundId;

                    Advance(operation, "CREDITED");

                    break;
                }

                case "CREDITED":
                {
                    if (order.Status != OrderStatus.Refunding ||
                        operation.RefundId is null ||
                        operation.RefundId == Guid.Empty)
                    {
                        Retry(operation, "REFUND_STATE_CONFLICT");
                        break;
                    }

                    var (result, error) =
                        await client.FinalizeAsync(operation, ct);

                    if (result is null)
                    {
                        Retry(operation, error);
                        break;
                    }

                    if (result.OrderId != operation.OrderId ||
                        result.RequestId != operation.RequestId ||
                        result.Status != "FINALIZED")
                    {
                        Retry(operation, "INVALID_FINALIZE_RESPONSE");
                        break;
                    }

                    order.Status = OrderStatus.Refunded;

                    db.OutboxMessages.Add(new OutboxMessage
                    {
                        AggregateId = order.Id,
                        RoutingKey = EventRoutes.OrderRefunded,
                        Payload = JsonSerializer.Serialize(
                            new OrderRefundedEvent(
                                order.Id,
                                order.KeycloakUserId,
                                order.CustomerEmail,
                                order.TotalAmount),
                            new JsonSerializerOptions(
                                JsonSerializerDefaults.Web)),
                        CreatedAtUtc = DateTime.UtcNow,
                        NextRetryAtUtc = DateTime.UtcNow
                    });

                    Advance(operation, "COMPLETED");

                    break;
                }
            }
        }

        operation.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation(
            "Refund {RequestId} for order {OrderId}: {Status}, retry {RetryCount}",
            operation.RequestId,
            operation.OrderId,
            operation.Status,
            operation.RetryCount);

        return true;
    }
}
