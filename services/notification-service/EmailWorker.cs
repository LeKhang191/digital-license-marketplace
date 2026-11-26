using Microsoft.EntityFrameworkCore;

namespace Notification.Worker;

public sealed class EmailWorker(
    IConfiguration config,
    IServiceScopeFactory scopes,
    EmailSender sender,
    ILogger<EmailWorker> log
) : BackgroundService
{
    private const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(
        CancellationToken ct)
    {
        if (!config.GetValue<bool>("Smtp:Enabled"))
        {
            log.LogInformation("SMTP is disabled");
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await ProcessNextAsync(ct))
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(2), ct);
                }
            }
            catch (OperationCanceledException)
                when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning(
                    "Notification worker failed: {Error}",
                    ex.GetType().Name);

                await Task.Delay(
                    TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task<bool> ProcessNextAsync(
        CancellationToken ct)
    {
        await using var scope =
            scopes.CreateAsyncScope();

        var db = scope.ServiceProvider
            .GetRequiredService<NotificationDbContext>();

        await using var transaction =
            await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;

        var rows = await db.Messages
            .FromSqlInterpolated(
                $"""
                SELECT *
                FROM notification_messages
                WHERE "Status" = 'PENDING'
                  AND "NextRetryAtUtc" <= {now}
                ORDER BY "CreatedAtUtc", "Id"
                LIMIT 1
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var message = rows.FirstOrDefault();

        if (message is null)
        {
            await transaction.CommitAsync(ct);
            return false;
        }

        try
        {
            await sender.SendAsync(message, ct);

            message.Status = "SENT";
            message.SentAtUtc = DateTime.UtcNow;
            message.LastError = null;
        }
        catch (OperationCanceledException)
            when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ex is System.Net.Mail.SmtpException smtp)
            {
                log.LogWarning(
                    "SMTP failure: StatusCode={StatusCode}, InnerException={InnerException}, SocketError={SocketError}",
                    smtp.StatusCode,
                    smtp.InnerException?.GetType().Name,
                    (smtp.InnerException as System.Net.Sockets.SocketException)?.SocketErrorCode);
            }
            else
            {
                log.LogWarning(
                    "Email failure: {Error}, InnerException={InnerException}",
                    ex.GetType().Name,
                    ex.InnerException?.GetType().Name);
            }

            message.RetryCount++;
            message.LastError = ex.GetType().Name;

            if (message.RetryCount >= MaxAttempts)
            {
                message.Status = "FAILED";
            }
            else
            {
                var delay = 15 *
                    (1 << Math.Min(message.RetryCount - 1, 4));

                message.NextRetryAtUtc =
                    DateTime.UtcNow.AddSeconds(delay);
            }
        }

        await db.SaveChangesAsync(ct);

        await transaction.CommitAsync(ct);

        log.LogInformation(
            "Notification {OrderId} {Type}: {Status}, failures={RetryCount}",
            message.OrderId,
            message.Type,
            message.Status,
            message.RetryCount);

        return true;
    }
}
