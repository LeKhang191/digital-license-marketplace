using System.Text.Json;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Kinguin;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace LicenseVault.Api.Infrastructure.Messaging;

// Separate simulated-payment route. Never consumes order.paid or emits key.assigned.
public sealed class KinguinMockWorkflowWorker(
    IConfiguration config, IOptions<RabbitMqOptions> options,
    IServiceScopeFactory scopes, RabbitMqPublisher publisher,
    ILogger<KinguinMockWorkflowWorker> log) : BackgroundService
{
    private const string Route = "kinguin.mock.payment-confirmed";
    private const string Queue = "license-vault.kinguin-mock-payment";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public sealed record MockPayment(Guid RequestId);
    private bool Enabled => config["Kinguin:Mode"] == "Mock";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        await Task.WhenAll(ConsumeAsync(ct), DispatchAsync(ct));
    }

    // The pending row is the durable outbox. Re-publish until the consumer commits a terminal result.
    private async Task DispatchAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
                var now = DateTime.UtcNow;
                var ids = await db.KinguinMockOrders.AsNoTracking()
                    .Where(x => (x.Status == "MOCK_PENDING" || x.Status == "MOCK_IMPORT_PENDING") && x.NextPublishAtUtc <= now)
                    .OrderBy(x => x.NextPublishAtUtc).ThenBy(x => x.Id).Select(x => x.Id).Take(20).ToListAsync(ct);
                foreach (var id in ids)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    timeout.CancelAfter(TimeSpan.FromSeconds(10));
                    await publisher.PublishAsync(Route, new MockPayment(id), timeout.Token);
                    var next = DateTime.UtcNow.AddSeconds(30);
                    await db.KinguinMockOrders.Where(x => x.Id == id && (x.Status == "MOCK_PENDING" || x.Status == "MOCK_IMPORT_PENDING"))
                        .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextPublishAtUtc, (DateTime?)next), ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning("Mock dispatch retry: {Error}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }
    }

    private async Task ConsumeAsync(CancellationToken ct)
    {
        var o = options.Value;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var factory = new ConnectionFactory { HostName = o.Host, Port = o.Port,
                    UserName = o.User, Password = o.Password, AutomaticRecoveryEnabled = false };
                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await channel.ExchangeDeclareAsync(o.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
                await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false,
                    arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum" }, cancellationToken: ct);
                await channel.QueueBindAsync(Queue, o.Exchange, Route, cancellationToken: ct);
                await channel.BasicQosAsync(0, 1, false, ct);
                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += async (_, args) =>
                {
                    try
                    {
                        var message = JsonSerializer.Deserialize<MockPayment>(args.Body.Span, Json);
                        if (args.RoutingKey != Route || message is null || message.RequestId == Guid.Empty)
                            throw new JsonException("Invalid mock event");
                        await ProcessAsync(message.RequestId, ct);
                        await channel.BasicAckAsync(args.DeliveryTag, false, ct);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
                    catch (JsonException) { await channel.BasicNackAsync(args.DeliveryTag, false, false, ct); }
                    catch (Exception ex)
                    {
                        log.LogWarning("Mock consumer retry: {Error}", ex.GetType().Name);
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        // Durable pending row will publish again; avoid a hot requeue loop.
                        await channel.BasicNackAsync(args.DeliveryTag, false, false, ct);
                    }
                };
                await channel.BasicConsumeAsync(Queue, autoAck: false, consumer: consumer, cancellationToken: ct);
                while (connection.IsOpen && channel.IsOpen && !ct.IsCancellationRequested)
                    await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning("Mock queue reconnect: {Error}", ex.GetType().Name); }
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
    }

    private async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<LicenseDbContext>();
        var supplier = scope.ServiceProvider.GetRequiredService<KinguinCatalog>();
        if (supplier.Mode != "Mock") return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var requestLock = $"kinguin-mock-request:{id:N}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({requestLock}, 0))", ct);
        var order = await db.KinguinMockOrders.SingleOrDefaultAsync(x => x.Id == id, ct);
        if (order is null || (order.Status != "MOCK_PENDING" && order.Status != "MOCK_IMPORT_PENDING")) return;
        var import = order.Status == "MOCK_IMPORT_PENDING";
        if (import && (!supplier.DemoAutoImport || order.SupplierProductId != await supplier.SuggestedMockIdAsync(order.ProductId, ct)))
        {
            order.Status = "MOCK_IMPORT_BLOCKED";
            order.NextPublishAtUtc = null;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return;
        }
        var quote = await supplier.GetAsync(order.SupplierProductId, ct);
        var stockLock = $"kinguin-mock-stock:{order.SupplierProductId}";
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({stockLock}, 0))", ct);
        var used = await db.KinguinMockOrders.Where(x => x.SupplierProductId == order.SupplierProductId && x.Status == "MOCK_FULFILLED")
            .SumAsync(x => (long)x.Quantity, ct);
        if (quote is null || used + order.Quantity > quote.TextQty)
            order.Status = "MOCK_OUT_OF_STOCK";
        else
        {
            var encryption = scope.ServiceProvider.GetRequiredService<ILicenseEncryption>();
            var keys = Enumerable.Range(1, order.Quantity)
                .Select(i => $"DEMO-NOT-REDEEMABLE-{order.Id:N}-{i}").ToArray();
            if (import)
            {
                // Same transaction and request lock as fulfillment; no separate import commit.
                // Stable batch ID makes request replay traceable without another schema change.
                const string name = "Kinguin Mock (DEMO)";
                const string normalized = "kinguin mock (demo)";
                var now = DateTime.UtcNow;
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    INSERT INTO suppliers ("Id", "Name", "NormalizedName", "IsActive", "CreatedAtUtc", "UpdatedAtUtc")
                    VALUES ({Guid.NewGuid()}, {name}, {normalized}, {true}, {now}, {now})
                    ON CONFLICT ("NormalizedName") DO NOTHING
                    """, ct);
                var sources = await db.Set<Supplier>().FromSqlInterpolated($"SELECT * FROM suppliers WHERE \"NormalizedName\" = {normalized} FOR UPDATE").ToListAsync(ct);
                var source = sources.Single();
                if (!source.IsActive)
                {
                    order.Status = "MOCK_IMPORT_BLOCKED";
                    order.NextPublishAtUtc = null;
                    await db.SaveChangesAsync(ct);
                    await transaction.CommitAsync(ct);
                    return;
                }
                var batch = new LicenseImportBatch { Id = order.Id, ProductId = order.ProductId,
                    SupplierId = source.Id, SupplierName = source.Name, Reference = order.Id.ToString(),
                    UnitCostVnd = quote.Price * 30000m, Method = "MOCK_API", ImportedBy = order.OwnerId,
                    CreatedAtUtc = now, Total = keys.Length, Imported = keys.Length };
                db.Add(batch);
                foreach (var key in keys)
                    db.LicenseKeys.Add(new LicenseKey { ProductId = order.ProductId, ImportBatchId = batch.Id,
                        EncryptedKey = encryption.Encrypt(key), KeyFingerprint = encryption.Fingerprint(key),
                        Status = LicenseStatus.Available, CreatedAtUtc = now });
            }
            order.EncryptedKeys = import ? "[]" : JsonSerializer.Serialize(keys.Select(encryption.Encrypt).ToArray());
            order.TotalCost = quote.Price * order.Quantity;
            order.Status = "MOCK_FULFILLED";
        }
        order.NextPublishAtUtc = null;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
    }
}
