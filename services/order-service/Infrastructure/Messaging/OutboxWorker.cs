using Marketplace.Shared.Messaging;

using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Messaging;

public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    ILogger<OutboxWorker> log
) : BackgroundService
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override Task ExecuteAsync(
        CancellationToken stoppingToken)
        => OutboxLoop.RunAsync(
            ProcessNextAsync, log, stoppingToken);

    private async Task<bool> ProcessNextAsync(
        CancellationToken ct)
    {
        await using var scope =
            scopeFactory.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<OrderDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        var messages = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT *
                FROM outbox_messages
                WHERE "PublishedAtUtc" IS NULL
                  AND "NextRetryAtUtc" <= {now}
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var message = messages.FirstOrDefault();

        if (message is null)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        try
        {
            using var timeout =
                CancellationTokenSource
                    .CreateLinkedTokenSource(ct);

            timeout.CancelAfter(
                TimeSpan.FromSeconds(15));

            await PublishAsync(
                message,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            message.RetryCount++;

            var delay = RetryDelay.Seconds(message.RetryCount);

            message.NextRetryAtUtc =
                DateTime.UtcNow.AddSeconds(delay);

            message.LastError =
                ex.GetType().Name;

            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            log.LogWarning(
                "Outbox event {EventId} failed. Retry {RetryCount} in {Delay}s",
                message.Id,
                message.RetryCount,
                delay);

            return true;
        }

        message.PublishedAtUtc = DateTime.UtcNow;
        message.LastError = null;

        await db.SaveChangesAsync(ct);

        if (message.RoutingKey == EventRoutes.OrderPaid)
        {
            await db.Orders
                .Where(o =>
                    o.Id == message.AggregateId &&
                    o.PaidEventSentAtUtc == null)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(
                        o => o.PaidEventSentAtUtc,
                        message.PublishedAtUtc),
                    ct);
        }

        await transaction.CommitAsync(ct);

        log.LogInformation(
            "Outbox event {EventId} published: {RoutingKey}",
            message.Id,
            message.RoutingKey);

        return true;
    }

    private async Task PublishAsync(
        OutboxMessage message,
        CancellationToken ct)
    {
        switch (message.RoutingKey)
        {
            case EventRoutes.OrderPaid:
            {
                var evt =
                    JsonSerializer.Deserialize<OrderPaidEvent>(
                        message.Payload,
                        Json)
                    ?? throw new InvalidOperationException(
                        "Invalid OrderPaidEvent payload");

                await publisher.PublishAsync(
                    message.RoutingKey,
                    evt,
                    ct);

                break;
            }

            case EventRoutes.OrderCompleted:
            {
                var evt =
                    JsonSerializer.Deserialize<OrderCompletedEvent>(
                        message.Payload,
                        Json)
                    ?? throw new InvalidOperationException(
                        "Invalid OrderCompletedEvent payload");

                await publisher.PublishAsync(
                    message.RoutingKey,
                    evt,
                    ct);

                break;
            }

            case EventRoutes.OrderRefunded:
            {
                var evt =
                    JsonSerializer.Deserialize<OrderRefundedEvent>(
                        message.Payload,
                        Json)
                    ?? throw new InvalidOperationException(
                        "Invalid OrderRefundedEvent payload");

                await publisher.PublishAsync(
                    message.RoutingKey,
                    evt,
                    ct);

                break;
            }

            default:
                throw new InvalidOperationException(
                    "Unsupported outbox routing key");
        }
    }
}
