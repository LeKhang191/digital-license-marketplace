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
            """);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task ImportAsync(Guid product)
    {
        // Separate derived keys for encryption and duplicate detection.
        var enc = HMACSHA256.HashData(masterKey, "vault-encryption-v1"u8);
        var mac = HMACSHA256.HashData(masterKey, "vault-fingerprint-v1"u8);
        await using var conn = await source.OpenConnectionAsync();
        await using var tx = await conn.BeginTransactionAsync();
        int added = 0, duplicates = 0;
        while (await Console.In.ReadLineAsync() is { } line)
        {
            var value = line.Trim();
            if (value.Length == 0) continue;
            if (value.Length > 512) throw new InvalidOperationException("Key too long");
            var plain = Encoding.UTF8.GetBytes(value);
            var fingerprint = HMACSHA256.HashData(mac, Encoding.UTF8.GetBytes(value.ToUpperInvariant()));
            var nonce = RandomNumberGenerator.GetBytes(12);
            var cipher = new byte[plain.Length];
            var tag = new byte[16];
            using (var aes = new AesGcm(enc, 16)) aes.Encrypt(nonce, plain, cipher, tag);
            CryptographicOperations.ZeroMemory(plain);
            await using var cmd = new NpgsqlCommand("""
                INSERT INTO vault_keys(id,product_id,fingerprint,encrypted_key)
                VALUES (@id,@product,@fingerprint,@encrypted) ON CONFLICT (fingerprint) DO NOTHING
                """, conn, tx);
            cmd.Parameters.AddWithValue("id", Guid.NewGuid());
            cmd.Parameters.AddWithValue("product", product);
            cmd.Parameters.AddWithValue("fingerprint", fingerprint);
            cmd.Parameters.AddWithValue("encrypted", nonce.Concat(tag).Concat(cipher).ToArray());
            if (await cmd.ExecuteNonQueryAsync() == 1) added++; else duplicates++;
        }
        await tx.CommitAsync();
        Console.WriteLine($"Imported={added}; duplicates={duplicates}");
    }

    public async Task<bool?> AllocateAsync(OrderPaidEvent evt, CancellationToken ct)
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
            if (await cmd.ExecuteScalarAsync(ct) is bool previous) return previous;
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
                return null;
            }
        }
        await using (var cmd = new NpgsqlCommand("INSERT INTO vault_fulfillments(order_id,user_id,delivered) VALUES(@order,@user,true)", conn, tx))
        {
            cmd.Parameters.AddWithValue("order", evt.OrderId);
            cmd.Parameters.AddWithValue("user", evt.KeycloakUserId);
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
        return true;
    }
}
