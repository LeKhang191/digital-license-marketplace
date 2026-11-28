using System.Text;
using Marketplace.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Services;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Infrastructure.Workers;

public sealed class PartnerWebhookWorker(
    IServiceScopeFactory scopes,
    IHttpClientFactory clients,
    IConfiguration config,
    ILogger<PartnerWebhookWorker> log
) : BackgroundService
{
    private const int MaxAttempts = 8;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        log.LogInformation("Partner Webhook Worker started");

        await OutboxLoop.RunAsync(ProcessNextAsync, log, ct);
    }

    private async Task<bool> ProcessNextAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();

        await using var tx = await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        var rows = await db.PartnerWebhookDeliveries
            .FromSqlInterpolated($"""
                SELECT *
                FROM partner_webhook_deliveries
                WHERE "Status" = 'PENDING'
                  AND "NextRetryAtUtc" <= {now}
                ORDER BY "NextRetryAtUtc", "CreatedAtUtc", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var delivery = rows.SingleOrDefault();
        if (delivery is null)
        {
            await tx.CommitAsync(ct);
            return false;
        }

        var endpoint = await db.PartnerWebhookEndpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PartnerId == delivery.PartnerId, ct);

        if (endpoint is null || !endpoint.IsActive || string.IsNullOrWhiteSpace(endpoint.CallbackUrl))
        {
            delivery.Status = "SKIPPED";
            delivery.LastError = "NO_ACTIVE_WEBHOOK_ENDPOINT";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        var allowPrivate = config.GetValue<bool>("PartnerWebhooks:AllowPrivateNetwork");
        var allowHttp = config.GetValue<bool>("PartnerWebhooks:AllowInsecureHttp");

        var (validatedUri, validationError) = await PartnerWebhookHelper.ValidateCallbackUrlAsync(
            endpoint.CallbackUrl,
            allowPrivate,
            allowHttp,
            ct);

        if (validatedUri is null)
        {
            delivery.AttemptCount++;
            delivery.LastError = validationError ?? "INVALID_CALLBACK_URL";
            delivery.Status = "FAILED";
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            return true;
        }

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = PartnerWebhookHelper.ComputeSignature(
            endpoint.SigningSecret,
            timestamp,
            delivery.Payload);

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, validatedUri)
            {
                Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
            };

            req.Headers.Add("X-DLM-Event", delivery.EventType);
            req.Headers.Add("X-DLM-Delivery-Id", delivery.Id.ToString("D"));
            req.Headers.Add("X-DLM-Timestamp", timestamp.ToString());
            req.Headers.Add("X-DLM-Signature", signature);

            using var response = await clients
                .CreateClient("PartnerWebhook")
                .SendAsync(req, ct);

            delivery.LastStatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = "DELIVERED";
                delivery.DeliveredAtUtc = DateTime.UtcNow;
                delivery.LastError = null;
            }
            else
            {
                ScheduleRetry(delivery, $"HTTP_{(int)response.StatusCode}");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            ScheduleRetry(delivery, ex.GetType().Name);
        }

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        log.LogInformation(
            "Partner webhook {DeliveryId} ({PartnerId} / {EventType}): {Status}, attempt={Attempt}, http={Code}",
            delivery.Id,
            delivery.PartnerId,
            delivery.EventType,
            delivery.Status,
            delivery.AttemptCount,
            delivery.LastStatusCode);

        return true;
    }

    private static void ScheduleRetry(
        Domain.Entities.PartnerWebhookDelivery delivery,
        string error)
    {
        delivery.AttemptCount++;
        delivery.LastError = error[..Math.Min(error.Length, 500)];

        if (delivery.AttemptCount >= MaxAttempts)
        {
            delivery.Status = "FAILED";
        }
        else
        {
            var delay = RetryDelay.Seconds(delivery.AttemptCount);
            delivery.NextRetryAtUtc = DateTime.UtcNow.AddSeconds(delay);
        }
    }
}
