
using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Messaging;

public sealed class OutboxWorker(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    ILogger<OutboxWorker> log
) : BackgroundService
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        log.LogInformation("Outbox Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await ProcessNextAsync(
                    stoppingToken);

                if (!processed)
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2),
                        stoppingToken);
                }
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogError(
                    ex,
                    "Outbox processing failed");

                await Task.Delay(
                    TimeSpan.FromSeconds(5),
                    stoppingToken);
            }
        }
    }

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

            var delay = Math.Min(
                300,
                Math.Pow(
                    2,
                    Math.Min(message.RetryCount, 8)));

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
