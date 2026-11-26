using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

public sealed class OrderPaymentService(
    OrderDbContext db)
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    private async Task<Order> LockOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        if (orderId == Guid.Empty)
            throw new InvalidDataException("Invalid order ID");

        var rows = await db.Orders
            .FromSqlInterpolated($"""
                SELECT *
                FROM orders
                WHERE "Id" = {orderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        return rows.SingleOrDefault()
            ?? throw new InvalidDataException(
                "Order not found");
    }

    public async Task ProcessSucceededAsync(
        PaymentSucceededEvent evt,
        CancellationToken ct)
    {
        var method = evt.PaymentMethod ?? "VNPAY";

        if (evt.PaymentId == Guid.Empty ||
            evt.Amount <= 0 ||
            string.IsNullOrWhiteSpace(evt.PaymentRef) ||
            method is not ("VNPAY" or "WALLET") ||
            (method == "VNPAY" &&
                (string.IsNullOrWhiteSpace(evt.TransactionNo) ||
                 string.IsNullOrWhiteSpace(evt.PayDate))) ||
            (method == "WALLET" &&
                (evt.TransactionNo is not null ||
                 evt.PayDate is not null ||
                 evt.PaymentRef != $"W{evt.OrderId:N}")) ||
            evt.PaidAtUtc.Kind != DateTimeKind.Utc)
            throw new InvalidDataException(
                "Invalid payment event");

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var order = await LockOrderAsync(
            evt.OrderId, ct);

        if (order.KeycloakUserId != evt.CustomerId ||
            order.TotalAmount != evt.Amount ||
            order.PaymentMethod != method ||
            order.PaymentRef != evt.PaymentRef)
            throw new InvalidDataException(
                "Payment does not match order");

        if (order.PaidAtUtc != null)
        {
            if (order.VnpTransactionNo != evt.TransactionNo)
                throw new InvalidDataException(
                    "Conflicting payment transaction");

            await transaction.CommitAsync(ct);
            return;
        }

        if (order.Status != OrderStatus.Pending &&
            order.Status != OrderStatus.Canceled)
            throw new InvalidDataException(
                "Unexpected order status");

        await db.Entry(order)
            .Collection(x => x.Items)
            .LoadAsync(ct);

        if (order.Items.Count == 0)
            throw new InvalidDataException(
                "Order has no items");

        order.Status = OrderStatus.Paid;
        order.PaidAtUtc = evt.PaidAtUtc;
        order.VnpTransactionNo = evt.TransactionNo;
        order.VnpPayDate = evt.PayDate;

        var paidEvent = new OrderPaidEvent(
            order.Id,
            order.KeycloakUserId,
            order.CustomerEmail,
            order.TotalAmount,
            order.Items.Select(i =>
                new OrderItemRef(i.Id, i.ProductId))
                .ToList(),
            evt.PaidAtUtc);

        db.OutboxMessages.Add(new OutboxMessage
        {
            AggregateId = order.Id,
            RoutingKey = EventRoutes.OrderPaid,
            Payload = JsonSerializer.Serialize(
                paidEvent, Json),
            CreatedAtUtc = DateTime.UtcNow,
            NextRetryAtUtc = DateTime.UtcNow
        });

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }

    public async Task ProcessFailedAsync(
        PaymentFailedEvent evt,
        CancellationToken ct)
    {
        if (evt.PaymentId == Guid.Empty ||
            string.IsNullOrWhiteSpace(evt.PaymentRef))
            throw new InvalidDataException(
                "Invalid failed payment event");

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var order = await LockOrderAsync(
            evt.OrderId, ct);

        if (order.KeycloakUserId != evt.CustomerId ||
            order.PaymentMethod != "VNPAY" ||
            order.PaymentRef != evt.PaymentRef)
            throw new InvalidDataException(
                "Payment does not match order");

        if (order.Status == OrderStatus.Pending &&
            order.PaidAtUtc == null)
        {
            order.Status = OrderStatus.Canceled;
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);
    }

    public async Task ProcessRefundedAsync(
        PaymentRefundedEvent evt,
        CancellationToken ct)
    {
        if (evt.RefundId == Guid.Empty ||
            evt.RequestId == Guid.Empty ||
            evt.PaymentId == Guid.Empty ||
            evt.Amount <= 0 ||
            evt.RefundedAtUtc.Kind != DateTimeKind.Utc)
        {
            throw new InvalidDataException(
                "Invalid refund event");
        }

        await using var tx =
            await db.Database.BeginTransactionAsync(ct);

        var operations = await db.RefundOperations
            .FromSqlInterpolated($"""
                SELECT *
                FROM refund_operations
                WHERE "OrderId" = {evt.OrderId}
                FOR UPDATE
                """)
            .ToListAsync(ct);

        var operation = operations.SingleOrDefault();

        if (operation is null ||
            operation.RequestId != evt.RequestId ||
            operation.CustomerId != evt.CustomerId ||
            operation.PaymentRef != evt.PaymentRef)
        {
            throw new InvalidDataException(
                "Refund operation mismatch");
        }

        var order = await LockOrderAsync(evt.OrderId, ct);

        if (order.TotalAmount != evt.Amount ||
            order.KeycloakUserId != evt.CustomerId ||
            order.PaymentRef != evt.PaymentRef)
        {
            throw new InvalidDataException(
                "Refund order mismatch");
        }

        if (operation.Status is "CREDITED" or "COMPLETED")
        {
            if (operation.RefundId != evt.RefundId)
                throw new InvalidDataException(
                    "Conflicting refund ID");

            await tx.CommitAsync(ct);
            return;
        }

        if (operation.Status != "PREPARED" ||
            order.Status != OrderStatus.Refunding)
        {
            throw new InvalidDataException(
                "Unexpected refund state");
        }

        operation.RefundId = evt.RefundId;
        operation.Status = "CREDITED";
        operation.RetryCount = 0;
        operation.LastError = null;
        operation.NextRetryAtUtc = DateTime.UtcNow;
        operation.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
