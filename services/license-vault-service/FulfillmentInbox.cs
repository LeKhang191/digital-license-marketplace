using System.Security.Cryptography;
using System.Text.Json;
using Contracts.Events;
using Npgsql;

// Durable inbox: persist before acknowledging the broker. Delayed jobs and
// dead letters live in PostgreSQL, not at the head of the RabbitMQ queue.
public sealed class FulfillmentInbox(NpgsqlDataSource source, int maxAttempts = 20, int retryDelaySeconds = 60)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public async Task InitializeAsync()
    {
        await using var cmd = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS vault_paid_inbox (
              order_id uuid PRIMARY KEY, payload text NOT NULL,
              status text NOT NULL DEFAULT 'PENDING' CHECK (status IN ('PENDING','DONE','DEAD')),
              attempts integer NOT NULL DEFAULT 0,
              next_attempt_at timestamptz NOT NULL DEFAULT now(),
              last_error text, created_at timestamptz NOT NULL DEFAULT now(),
              updated_at timestamptz NOT NULL DEFAULT now()
            );
            ALTER TABLE vault_paid_inbox ADD COLUMN IF NOT EXISTS missing_product_id uuid;
            ALTER TABLE vault_paid_inbox ADD COLUMN IF NOT EXISTS out_of_stock_sent_at timestamptz;
            CREATE INDEX IF NOT EXISTS ix_vault_paid_inbox_due
              ON vault_paid_inbox(next_attempt_at,order_id) WHERE status='PENDING';
            CREATE INDEX IF NOT EXISTS ix_vault_paid_inbox_out_of_stock
              ON vault_paid_inbox(updated_at,order_id) WHERE status='DEAD' AND last_error='INSUFFICIENT_STOCK' AND out_of_stock_sent_at IS NULL;
            CREATE TABLE IF NOT EXISTS vault_paid_rejected (
              fingerprint text PRIMARY KEY, reason text NOT NULL,
              created_at timestamptz NOT NULL DEFAULT now()
            );
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public static OrderPaidEvent? Parse(ReadOnlyMemory<byte> body)
    {
        try
        {
            var evt = JsonSerializer.Deserialize<OrderPaidEvent>(body.Span, Json);
            if (evt is null || evt.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(evt.KeycloakUserId)
                || evt.Items is null || evt.Items.Count == 0
                || evt.Items.Any(i => i is null || i.ItemId == Guid.Empty || i.ProductId == Guid.Empty)
                || evt.Items.Select(i => i.ItemId).Distinct().Count() != evt.Items.Count)
                return null;
            return evt;
        }
        catch (JsonException) { return null; }
    }

    public async Task AcceptAsync(OrderPaidEvent evt, CancellationToken ct)
    {
        // Remove customer email; retain the existing event contract.
        var payload = JsonSerializer.Serialize(evt with { CustomerEmail = null }, Json);
        await using var cmd = source.CreateCommand("""
            INSERT INTO vault_paid_inbox(order_id,payload) VALUES(@id,@payload)
            ON CONFLICT(order_id) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("id", evt.OrderId);
        cmd.Parameters.AddWithValue("payload", payload);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task RejectAsync(ReadOnlyMemory<byte> body, CancellationToken ct)
    {
        // Store a fingerprint, not arbitrary untrusted message content.
        var hash = Convert.ToHexString(SHA256.HashData(body.Span));
        await using var cmd = source.CreateCommand("""
            INSERT INTO vault_paid_rejected(fingerprint,reason) VALUES(@hash,'INVALID_PAYLOAD')
            ON CONFLICT(fingerprint) DO NOTHING
            """);
        cmd.Parameters.AddWithValue("hash", hash);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<bool> ProcessOneAsync(VaultStore store, ILogger log, CancellationToken ct)
    {
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        Guid id;
        string payload;
        int attempts;
        await using (var cmd = new NpgsqlCommand("""
            SELECT order_id,payload,attempts FROM vault_paid_inbox
            WHERE status='PENDING' AND next_attempt_at <= now()
            ORDER BY next_attempt_at,order_id LIMIT 1 FOR UPDATE SKIP LOCKED
            """, conn, tx))
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (!await reader.ReadAsync(ct)) return false;
            id = reader.GetGuid(0);
            payload = reader.GetString(1);
            attempts = reader.GetInt32(2);
        }
        var evt = Parse(System.Text.Encoding.UTF8.GetBytes(payload));
        var done = false;
        string? reason = null;
        Guid? missingProductId = null;
        if (evt is null || evt.OrderId != id) reason = "INVALID_PAYLOAD";
        else
        {
            try
            {
                var (allocated, missingProduct) = await store.AllocateAsync(evt, ct);
                done = allocated;
                if (!done)
                {
                    reason = "INSUFFICIENT_STOCK";
                    missingProductId = missingProduct ?? evt.Items[0].ProductId;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                reason = "ALLOCATION_ERROR";
                log.LogWarning("Allocation error for order {OrderId}: {ExceptionType}", id, ex.GetType().Name);
            }
        }
        // The allocation transaction commits independently. If the process dies
        // here, AllocateAsync's existing order-id idempotency makes retry safe.
        var effectiveMaxAttempts = Math.Max(1, maxAttempts);
        var effectiveRetryDelay = Math.Max(5, retryDelaySeconds);
        var status = done ? "DONE" : attempts + 1 >= effectiveMaxAttempts || reason == "INVALID_PAYLOAD" ? "DEAD" : "PENDING";
        await using (var cmd = new NpgsqlCommand("""
            UPDATE vault_paid_inbox SET status=@status, attempts=attempts+1,
              next_attempt_at=now()+make_interval(secs => @delay), last_error=@reason,
              missing_product_id=@missingProduct,
              updated_at=now() WHERE order_id=@id
            """, conn, tx))
        {
            cmd.Parameters.AddWithValue("status", status);
            cmd.Parameters.AddWithValue("delay", effectiveRetryDelay);
            cmd.Parameters.AddWithValue("reason", NpgsqlTypes.NpgsqlDbType.Text, (object?)reason ?? DBNull.Value);
            cmd.Parameters.AddWithValue("missingProduct", NpgsqlTypes.NpgsqlDbType.Uuid, (object?)missingProductId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("id", id);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        if (done) log.LogInformation("Keys allocated for order {OrderId}", id);
        else log.LogWarning("Fulfillment {OrderId}: {Status}, {Reason}, attempt {Attempt}", id, status, reason, attempts + 1);
        return true;
    }

    public async Task<List<KeyOutOfStockEvent>> PendingOutOfStockAsync(CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT order_id, missing_product_id, last_error
            FROM vault_paid_inbox
            WHERE status='DEAD' AND last_error='INSUFFICIENT_STOCK' AND out_of_stock_sent_at IS NULL
            ORDER BY updated_at, order_id
            LIMIT 100
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var list = new List<KeyOutOfStockEvent>();
        while (await reader.ReadAsync(ct))
        {
            var orderId = reader.GetGuid(0);
            var productId = reader.IsDBNull(1) ? Guid.Empty : reader.GetGuid(1);
            var reason = reader.IsDBNull(2) ? "INSUFFICIENT_STOCK" : reader.GetString(2);
            list.Add(new KeyOutOfStockEvent(orderId, productId, reason));
        }
        return list;
    }

    public async Task MarkOutOfStockSentAsync(Guid orderId, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            UPDATE vault_paid_inbox
            SET out_of_stock_sent_at=now(), updated_at=now()
            WHERE order_id=@id AND out_of_stock_sent_at IS NULL
            """);
        cmd.Parameters.AddWithValue("id", orderId);
        await cmd.ExecuteNonQueryAsync(ct);
    }
}

public sealed class FulfillmentRetryWorker(FulfillmentInbox inbox, VaultStore store, ILogger<FulfillmentRetryWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!await inbox.ProcessOneAsync(store, log, ct)) await Task.Delay(1000, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex)
            {
                log.LogWarning("Fulfillment inbox unavailable: {ExceptionType}", ex.GetType().Name);
                await Task.Delay(5000, ct);
            }
        }
    }
}
