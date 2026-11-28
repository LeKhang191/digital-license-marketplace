using System.Security.Cryptography;
using System.Text;
using Contracts.Events;
using Npgsql;

public sealed class VaultStore(NpgsqlDataSource source, byte[] masterKey)
{
    public async Task InitializeAsync()
    {
        await using var cmd = source.CreateCommand("""
            CREATE TABLE IF NOT EXISTS vault_keys (
              id uuid PRIMARY KEY, product_id uuid NOT NULL,
              fingerprint bytea UNIQUE NOT NULL, encrypted_key bytea NOT NULL,
              order_id uuid, item_id uuid UNIQUE, allocated_at timestamptz,
              CHECK ((order_id IS NULL AND item_id IS NULL AND allocated_at IS NULL)
                  OR (order_id IS NOT NULL AND item_id IS NOT NULL AND allocated_at IS NOT NULL))
            );
            CREATE INDEX IF NOT EXISTS ix_vault_available ON vault_keys(product_id) WHERE order_id IS NULL;
            CREATE TABLE IF NOT EXISTS vault_fulfillments (
              order_id uuid PRIMARY KEY, user_id text NOT NULL,
              delivered boolean NOT NULL, created_at timestamptz NOT NULL DEFAULT now()
            );
            ALTER TABLE vault_fulfillments ADD COLUMN IF NOT EXISTS event_sent_at timestamptz;
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task<(int Added, int Duplicates)> ImportLinesAsync(Guid product, IEnumerable<string> lines, CancellationToken ct = default)
    {
        // Separate derived keys for encryption and duplicate detection.
        var enc = HMACSHA256.HashData(masterKey, "vault-encryption-v1"u8);
        var mac = HMACSHA256.HashData(masterKey, "vault-fingerprint-v1"u8);
        try
        {
            await using var conn = await source.OpenConnectionAsync(ct);
            await using var tx = await conn.BeginTransactionAsync(ct);
            int added = 0, duplicates = 0;
            foreach (var line in lines)
            {
                var value = line.Trim();
                if (value.Length == 0) continue;
                if (value.Length > 512) throw new InvalidOperationException("Key too long");
                var plain = Encoding.UTF8.GetBytes(value);
                var fingerprint = HMACSHA256.HashData(mac, Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
                var nonce = RandomNumberGenerator.GetBytes(12);
                var cipher = new byte[plain.Length];
                var tag = new byte[16];
                try
                {
                    using var aes = new AesGcm(enc, 16);
                    aes.Encrypt(nonce, plain, cipher, tag);
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
                await using var cmd = new NpgsqlCommand("""
                    INSERT INTO vault_keys(id,product_id,fingerprint,encrypted_key)
                    VALUES (@id,@product,@fingerprint,@encrypted) ON CONFLICT (fingerprint) DO NOTHING
                    """, conn, tx);
                cmd.Parameters.AddWithValue("id", Guid.NewGuid());
                cmd.Parameters.AddWithValue("product", product);
                cmd.Parameters.AddWithValue("fingerprint", fingerprint);
                cmd.Parameters.AddWithValue("encrypted", nonce.Concat(tag).Concat(cipher).ToArray());
                if (await cmd.ExecuteNonQueryAsync(ct) == 1) added++; else duplicates++;
            }
            await tx.CommitAsync(ct);
            return (added, duplicates);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(enc);
            CryptographicOperations.ZeroMemory(mac);
        }
    }

    public async Task ImportAsync(Guid product)
    {
        var lines = new List<string>();
        while (await Console.In.ReadLineAsync() is { } line) lines.Add(line);
        var (added, duplicates) = await ImportLinesAsync(product, lines);
        Console.WriteLine($"Imported={added}; duplicates={duplicates}");
    }

    public sealed record StockSummaryRow(Guid ProductId, int Available, int Allocated);
    public sealed record StockAvailabilityRow(Guid ProductId, int Available);

    public async Task<List<StockSummaryRow>> GetStockSummaryAsync(CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT product_id,
                   COUNT(*) FILTER (WHERE order_id IS NULL)::int AS available,
                   COUNT(*) FILTER (WHERE order_id IS NOT NULL)::int AS allocated
            FROM vault_keys
            GROUP BY product_id
            ORDER BY product_id
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<StockSummaryRow>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetInt32(1), reader.GetInt32(2)));
        return rows;
    }

    public async Task<List<StockAvailabilityRow>> GetAvailabilityAsync(CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT product_id, COUNT(*)::int AS available
            FROM vault_keys
            WHERE order_id IS NULL
            GROUP BY product_id
            ORDER BY product_id
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new List<StockAvailabilityRow>();
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetGuid(0), reader.GetInt32(1)));
        return rows;
    }

    public async Task<int> GetProductAvailabilityAsync(Guid productId, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT COUNT(*)::int
            FROM vault_keys
            WHERE product_id = @product AND order_id IS NULL
            """);
        cmd.Parameters.AddWithValue("product", productId);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct));
    }

    public async Task<(bool Allocated, Guid? MissingProductId)> AllocateAsync(OrderPaidEvent evt, CancellationToken ct)
    {
        if (evt.Items.Count == 0 || evt.Items.Select(i => i.ItemId).Distinct().Count() != evt.Items.Count)
            throw new InvalidOperationException("Invalid order items");
        await using var conn = await source.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        // Serialize allocation across consumers; small-shop implementation.
        await using (var cmd = new NpgsqlCommand("SELECT pg_advisory_xact_lock(74120933)", conn, tx))
            await cmd.ExecuteNonQueryAsync(ct);
        await using (var cmd = new NpgsqlCommand("SELECT delivered FROM vault_fulfillments WHERE order_id=@order", conn, tx))
        {
            cmd.Parameters.AddWithValue("order", evt.OrderId);
            if (await cmd.ExecuteScalarAsync(ct) is bool previous) return (previous, null);
        }
        foreach (var item in evt.Items.OrderBy(i => i.ProductId).ThenBy(i => i.ItemId))
        {
            await using var cmd = new NpgsqlCommand("""
                UPDATE vault_keys SET order_id=@order,item_id=@item,allocated_at=now()
                WHERE id=(SELECT id FROM vault_keys WHERE product_id=@product AND order_id IS NULL ORDER BY id LIMIT 1 FOR UPDATE)
                """, conn, tx);
            cmd.Parameters.AddWithValue("order", evt.OrderId);
            cmd.Parameters.AddWithValue("item", item.ItemId);
            cmd.Parameters.AddWithValue("product", item.ProductId);
            if (await cmd.ExecuteNonQueryAsync(ct) != 1)
            {
                // Roll back the entire order, retain the message for retry after restock.
                await tx.RollbackAsync(ct);
                return (false, item.ProductId);
            }
        }
        await using (var cmd = new NpgsqlCommand("INSERT INTO vault_fulfillments(order_id,user_id,delivered) VALUES(@order,@user,true)", conn, tx))
        {
            cmd.Parameters.AddWithValue("order", evt.OrderId);
            cmd.Parameters.AddWithValue("user", evt.KeycloakUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return (true, null);
    }
    // Existing delivered rows are automatically eligible for dispatch after upgrade.
    public async Task<List<LicenseDeliveredEvent>> PendingDeliveriesAsync(CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT f.order_id, f.user_id, k.item_id, k.product_id
            FROM (SELECT order_id,user_id FROM vault_fulfillments
                  WHERE delivered AND event_sent_at IS NULL ORDER BY created_at,order_id LIMIT 100) f
            JOIN vault_keys k ON k.order_id=f.order_id
            ORDER BY f.order_id,k.item_id
            """);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var rows = new Dictionary<Guid, (string User, List<OrderItemRef> Items)>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetGuid(0);
            if (!rows.TryGetValue(id, out var row))
                rows[id] = row = (reader.GetString(1), new List<OrderItemRef>());
            row.Items.Add(new(reader.GetGuid(2), reader.GetGuid(3)));
        }
        return rows.Select(r => new LicenseDeliveredEvent(r.Key, r.Value.User, r.Value.Items)).ToList();
    }

    public async Task MarkDeliverySentAsync(Guid id, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("UPDATE vault_fulfillments SET event_sent_at=now() WHERE order_id=@id AND event_sent_at IS NULL");
        cmd.Parameters.AddWithValue("id", id);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<List<PurchasedLicense>> GetKeysAsync(Guid orderId, string userId, CancellationToken ct)
    {
        await using var cmd = source.CreateCommand("""
            SELECT k.item_id,k.product_id,k.encrypted_key FROM vault_keys k
            JOIN vault_fulfillments f ON f.order_id=k.order_id
            WHERE f.order_id=@id AND f.user_id=@user AND f.delivered
            ORDER BY k.item_id
            """);
        cmd.Parameters.AddWithValue("id", orderId);
        cmd.Parameters.AddWithValue("user", userId);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var keys = new List<PurchasedLicense>();
        var enc = HMACSHA256.HashData(masterKey, "vault-encryption-v1"u8);
        try
        {
            using var aes = new AesGcm(enc, 16);
            while (await reader.ReadAsync(ct))
            {
                var blob = reader.GetFieldValue<byte[]>(2);
                if (blob.Length < 29) throw new CryptographicException("Invalid encrypted key");
                var plain = new byte[blob.Length - 28];
                try
                {
                    aes.Decrypt(blob.AsSpan(0,12), blob.AsSpan(28), blob.AsSpan(12,16), plain);
                    keys.Add(new(reader.GetGuid(0), reader.GetGuid(1), Encoding.UTF8.GetString(plain)));
                }
                finally { CryptographicOperations.ZeroMemory(plain); }
            }
        }
        finally { CryptographicOperations.ZeroMemory(enc); }
        return keys;
    }
}
