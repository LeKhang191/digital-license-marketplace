using System.Text.Json;
using Contracts.Events;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Infrastructure.Messaging;

public sealed class AllocationOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    ILogger<AllocationOutboxWorker> log
) : BackgroundService
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        log.LogInformation(
            "Allocation Outbox Worker started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed =
                    await ProcessNextAsync(stoppingToken);

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
                    "Allocation outbox processing failed");

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

            var delay = Math.Min(
                300,
                Math.Pow(
                    2,
                    Math.Min(result.RetryCount, 8)));

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
