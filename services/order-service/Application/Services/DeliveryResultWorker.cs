using System.Text.Json;
using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;
using RabbitMQ.Client;

namespace OrderService.Application.Services;

public sealed class DeliveryResultWorker(IServiceScopeFactory scopes, IOptions<RabbitMqOptions> options, ILogger<DeliveryResultWorker> log) : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var o = options.Value;
                var factory = new ConnectionFactory { HostName = o.Host, Port = o.Port, UserName = o.User, Password = o.Password };
                await using var connection = await factory.CreateConnectionAsync(ct);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
                await channel.ExchangeDeclareAsync(EventRoutes.Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
                const string rejectedDelivered = "order.license-delivered.rejected.v1";
                const string rejectedOutOfStock = "order.key-out-of-stock.rejected.v1";
                await channel.QueueDeclareAsync(rejectedDelivered, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueDeclareAsync(rejectedOutOfStock, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueDeclareAsync(EventRoutes.OrderDeliveryQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(EventRoutes.OrderDeliveryQueue, EventRoutes.Exchange, EventRoutes.LicenseDelivered, cancellationToken: ct);
                await channel.QueueDeclareAsync(EventRoutes.OrderOutOfStockQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
                await channel.QueueBindAsync(EventRoutes.OrderOutOfStockQueue, EventRoutes.Exchange, EventRoutes.KeyOutOfStock, cancellationToken: ct);
                // Manual rejection is retained in a separate durable queue, with confirmed publish.
                await using var quarantine = await connection.CreateChannelAsync(new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
                while (!ct.IsCancellationRequested)
                {
                    var handled = false;
                    var msg = await channel.BasicGetAsync(EventRoutes.OrderDeliveryQueue, autoAck: false, cancellationToken: ct);
                    if (msg is not null)
                    {
                        handled = true;
                        LicenseDeliveredEvent? evt;
                        try { evt = JsonSerializer.Deserialize<LicenseDeliveredEvent>(msg.Body.Span, Json); }
                        catch (JsonException) { evt = null; }
                        if (evt is null || !await ApplyDeliveredAsync(evt, ct))
                        {
                            await quarantine.BasicPublishAsync("", rejectedDelivered, mandatory: true,
                                basicProperties: new BasicProperties { Persistent = true }, body: msg.Body, cancellationToken: ct);
                            log.LogWarning("Delivery result rejected; inspect quarantine queue");
                        }
                        await channel.BasicAckAsync(msg.DeliveryTag, multiple: false, cancellationToken: ct);
                    }

                    var oosMsg = await channel.BasicGetAsync(EventRoutes.OrderOutOfStockQueue, autoAck: false, cancellationToken: ct);
                    if (oosMsg is not null)
                    {
                        handled = true;
                        KeyOutOfStockEvent? oosEvt;
                        try { oosEvt = JsonSerializer.Deserialize<KeyOutOfStockEvent>(oosMsg.Body.Span, Json); }
                        catch (JsonException) { oosEvt = null; }
                        if (oosEvt is null || !await ApplyOutOfStockAsync(oosEvt, ct))
                        {
                            await quarantine.BasicPublishAsync("", rejectedOutOfStock, mandatory: true,
                                basicProperties: new BasicProperties { Persistent = true }, body: oosMsg.Body, cancellationToken: ct);
                            log.LogWarning("Out-of-stock event rejected; inspect quarantine queue");
                        }
                        await channel.BasicAckAsync(oosMsg.DeliveryTag, multiple: false, cancellationToken: ct);
                    }

                    if (!handled) await Task.Delay(1000, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Delivery result worker failed; retrying"); }
            await Task.Delay(5000, ct);
        }
    }

    private async Task<bool> ApplyDeliveredAsync(LicenseDeliveredEvent evt, CancellationToken ct)
    {
        if (evt.Items is null || evt.Items.Count == 0 || evt.Items.Any(i => i is null)) return false;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = await db.Orders.AsNoTracking().Include(o => o.Items).SingleOrDefaultAsync(o => o.Id == evt.OrderId, ct);
        if (order is null || order.KeycloakUserId != evt.KeycloakUserId || order.PaidAtUtc is null ||
            !order.Items.Select(i => new OrderItemRef(i.Id, i.ProductId)).OrderBy(i => i.ItemId)
                .SequenceEqual(evt.Items.OrderBy(i => i.ItemId))) return false;
        if (order.Status == OrderStatus.Completed) return true;
        if (order.Status != OrderStatus.Paid) return false;
        var changed = await db.Orders.Where(o => o.Id == evt.OrderId && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Completed), ct);
        if (changed == 1) { log.LogInformation("Order {OrderId} completed", evt.OrderId); return true; }
        return await db.Orders.AnyAsync(o => o.Id == evt.OrderId && o.Status == OrderStatus.Completed, ct);
    }

    private async Task<bool> ApplyOutOfStockAsync(KeyOutOfStockEvent evt, CancellationToken ct)
    {
        if (evt.OrderId == Guid.Empty) return false;
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
        var order = await db.Orders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == evt.OrderId, ct);
        if (order is null || order.PaidAtUtc is null) return false;
        if (order.Status is OrderStatus.Refunding or OrderStatus.Refunded) return true;
        if (order.Status != OrderStatus.Paid) return false;
        var changed = await db.Orders.Where(o => o.Id == evt.OrderId && o.Status == OrderStatus.Paid)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, OrderStatus.Refunding), ct);
        if (changed == 1)
        {
            log.LogWarning("Order {OrderId} moved to REFUNDING due to {Reason} (product {ProductId})", evt.OrderId, evt.Reason, evt.ProductId);
            return true;
        }
        return await db.Orders.AnyAsync(o => o.Id == evt.OrderId && (o.Status == OrderStatus.Refunding || o.Status == OrderStatus.Refunded), ct);
    }
}
