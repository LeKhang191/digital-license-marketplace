using Contracts.Events;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Messaging;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

// At-least-once dispatch, not exactly-once. Run only one Order replica until
// consumers implement idempotency and multi-replica dispatch is coordinated.
public sealed class PaidOrderDispatchWorker(IServiceScopeFactory scopes,
    IEventPublisher publisher, ILogger<PaidOrderDispatchWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
                var orders = await db.Orders.AsNoTracking().Include(o => o.Items)
                    .Where(o => o.Status == OrderStatus.Paid && o.PaidEventSentAtUtc == null && o.PaidAtUtc != null)
                    .OrderBy(o => o.PaidAtUtc).ThenBy(o => o.Id).Take(100).ToListAsync(stoppingToken);
                foreach (var order in orders)
                {
                    var evt = new OrderPaidEvent(order.Id, order.KeycloakUserId, order.CustomerEmail,
                        order.TotalAmount, order.Items.Select(i => new OrderItemRef(i.Id, i.ProductId)).ToList(),
                        order.PaidAtUtc!.Value);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    await publisher.PublishAsync(EventRoutes.OrderPaid, evt, timeout.Token);
                    DateTime? sentAt = DateTime.UtcNow;
                    await db.Orders.Where(o => o.Id == order.Id && o.PaidEventSentAtUtc == null)
                        .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaidEventSentAtUtc, sentAt), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch (Exception ex) { log.LogWarning(ex, "Paid order dispatch failed; will retry"); }
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }
}
