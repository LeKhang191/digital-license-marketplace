using Marketplace.Shared.Messaging;
using System.Text.Json;
using Contracts.Events;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Messaging;

public sealed class AllocationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    RabbitMqPublisher publisher,
    ILogger<AllocationOutboxWorker> log
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
            .GetRequiredService<LicenseDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        var rows = await db.AllocationResults
            .FromSqlInterpolated($"""
                SELECT *
                FROM allocation_results
                WHERE "PublishedAtUtc" IS NULL
                  AND "NextRetryAtUtc" <= {now}
                ORDER BY "ProcessedAtUtc", "OrderId"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var result = rows.FirstOrDefault();

        if (result is null)
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
                result,
                timeout.Token);
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.RetryCount++;

            var delay = RetryDelay.Seconds(result.RetryCount);

            result.NextRetryAtUtc =
                DateTime.UtcNow.AddSeconds(delay);

            result.LastError =
                ex.GetType().Name;

            await db.SaveChangesAsync(ct);

            await transaction.CommitAsync(ct);

            log.LogWarning(
                "Allocation event for {OrderId} failed. Retry {RetryCount} in {Delay}s",
                result.OrderId,
                result.RetryCount,
                delay);

            return true;
        }

        result.PublishedAtUtc = DateTime.UtcNow;
        result.LastError = null;

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        log.LogInformation(
            "Allocation event published: {OrderId} {RoutingKey}",
            result.OrderId,
            result.RoutingKey);

        return true;
    }

    private async Task PublishAsync(
        AllocationResult result,
        CancellationToken ct)
    {
        switch (result.RoutingKey)
        {
            case EventRoutes.KeyAssigned:
            {
                var evt =
                    JsonSerializer.Deserialize<KeyAssignedEvent>(
                        result.Payload, Json)
                    ?? throw new InvalidDataException(
                        "Invalid KeyAssignedEvent payload.");

                await publisher.PublishAsync(
                    result.RoutingKey,
                    evt,
                    ct);

                break;
            }

            case EventRoutes.KeyOutOfStock:
            {
                var evt =
                    JsonSerializer.Deserialize<KeyOutOfStockEvent>(
                        result.Payload, Json)
                    ?? throw new InvalidDataException(
                        "Invalid KeyOutOfStockEvent payload.");

                await publisher.PublishAsync(
                    result.RoutingKey,
                    evt,
                    ct);

                break;
            }

            default:
                throw new InvalidOperationException(
                    "Unsupported allocation routing key.");
        }
    }
}
