using System.Security.Cryptography;
using System.Text;
using Marketplace.Shared.Security;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.DTOs;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Catalog;
using OrderService.Infrastructure.License;
using OrderService.Infrastructure.Payment;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

public sealed class CreatePartnerOrderRequest
{
    public Guid ProductId { get; set; }

    public int Quantity { get; set; } = 1;

    public string? ExternalReference { get; set; }
}

public sealed record PartnerOrderResponse(
    Guid OrderId,
    string PartnerId,
    string? ExternalReference,
    string Status,
    string FulfillmentStatus,
    decimal TotalAmount,
    string Currency,
    DateTime CreatedAtUtc,
    IReadOnlyList<OrderItemResponse> Items,
    bool AlreadyProcessed = false);

public sealed class PartnerOrderService(
    OrderDbContext db,
    ICatalogClient catalog,
    LicenseClient licenses,
    PaymentClient payments)
{
    public static bool IsValidIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var trimmed = value.Trim();
        if (trimmed.Length < 8 || trimmed.Length > 64)
            return false;

        foreach (var ch in trimmed)
        {
            if (ch < 0x21 || ch > 0x7E)
                return false;
        }

        return true;
    }

    public async Task<(PartnerOrderResponse? Order, string? Error)> CreateAndPayAsync(
        string partnerId,
        string idempotencyKey,
        CreatePartnerOrderRequest request,
        string clientIp,
        CancellationToken ct)
    {
        if (!KeycloakAuthentication.IsValidPartnerId(partnerId))
            return (null, "INVALID_PARTNER_ID");

        if (!IsValidIdempotencyKey(idempotencyKey))
            return (null, "INVALID_IDEMPOTENCY_KEY");

        if (request.ProductId == Guid.Empty ||
            request.Quantity < 1 ||
            request.Quantity > 10)
        {
            return (null, "INVALID_ORDER_REQUEST");
        }

        var externalRef = string.IsNullOrWhiteSpace(request.ExternalReference)
            ? null
            : request.ExternalReference.Trim();

        if (externalRef is not null && externalRef.Length > 128)
            return (null, "INVALID_EXTERNAL_REFERENCE");

        var key = idempotencyKey.Trim();
        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);
        var requestHash = ComputeRequestHash(request.ProductId, request.Quantity, externalRef);

        Order? order;
        var alreadyExisted = false;

        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var lockKey = $"partner-order:{partnerId}:{key}";
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                SELECT pg_advisory_xact_lock(
                    hashtextextended({lockKey}, 0)
                )
                """,
                ct);

            var existingReq = await db.PartnerOrderRequests
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    x => x.PartnerId == partnerId && x.IdempotencyKey == key,
                    ct);

            if (existingReq is not null)
            {
                if (!string.Equals(existingReq.RequestHash, requestHash, StringComparison.Ordinal))
                    return (null, "IDEMPOTENCY_CONFLICT");

                order = await db.Orders
                    .Include(o => o.Items)
                    .SingleOrDefaultAsync(o => o.Id == existingReq.OrderId, ct);

                if (order is null)
                    return (null, OrderErrors.OrderNotFound);

                alreadyExisted = true;
                await tx.CommitAsync(ct);
            }
            else
            {
                var product = await catalog.GetProductAsync(request.ProductId, ct);
                if (product is null || !product.IsActive)
                    return (null, OrderErrors.ProductNotFound);

                var available = await licenses.GetAvailableAsync(product.Id, ct);
                if (available is null)
                    return (null, OrderErrors.InventoryUnavailable);

                if (available.Value < request.Quantity)
                    return (null, OrderErrors.InsufficientStock);

                var now = DateTime.UtcNow;
                order = new Order
                {
                    KeycloakUserId = subject,
                    CustomerEmail = null,
                    CustomerEmailVerified = false,
                    PaymentMethod = "WALLET",
                    Status = OrderStatus.Pending,
                    TotalAmount = product.Price * request.Quantity,
                    CreatedAtUtc = now
                };

                order.PaymentRef = $"W{order.Id:N}";

                for (var i = 0; i < request.Quantity; i++)
                {
                    order.Items.Add(new OrderItem
                    {
                        OrderId = order.Id,
                        ProductId = product.Id,
                        Price = product.Price
                    });
                }

                db.Orders.Add(order);
                db.PartnerOrderRequests.Add(new PartnerOrderRequest
                {
                    PartnerId = partnerId,
                    IdempotencyKey = key,
                    RequestHash = requestHash,
                    ExternalReference = externalRef,
                    OrderId = order.Id,
                    CreatedAtUtc = now
                });

                await db.SaveChangesAsync(ct);
                await tx.CommitAsync(ct);
            }
        }

        // If order is still PENDING, debit the Partner Prepaid Wallet atomically (idempotent in Payment Service).
        if (order.Status == OrderStatus.Pending)
        {
            var paymentReq = new PaymentSessionRequest(
                order.Id,
                order.KeycloakUserId,
                order.TotalAmount,
                order.CreatedAtUtc,
                clientIp);

            var (payment, error) = await payments.PayWithWalletAsync(paymentReq, ct);

            if (payment is null)
            {
                return (null, error switch
                {
                    "INSUFFICIENT_WALLET_BALANCE" => OrderErrors.InsufficientWalletBalance,
                    "PAYMENT_EXPIRED" => OrderErrors.PaymentExpired,
                    "PAYMENT_NOT_PAYABLE" => OrderErrors.NotPayable,
                    _ => OrderErrors.PaymentUnavailable
                });
            }
        }

        var current = await GetAsync(partnerId, order.Id, ct);
        if (current is null)
            return (null, OrderErrors.OrderNotFound);

        return (current with { AlreadyProcessed = alreadyExisted }, null);
    }

    public async Task<PartnerOrderResponse?> GetAsync(
        string partnerId,
        Guid orderId,
        CancellationToken ct)
    {
        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);

        var order = await db.Orders
            .AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(
                o => o.Id == orderId && o.KeycloakUserId == subject,
                ct);

        if (order is null)
            return null;

        var fulfillmentStatus = await db.FulfillmentResults
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .Select(x => x.Status)
            .FirstOrDefaultAsync(ct);

        var externalRef = await db.PartnerOrderRequests
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .Select(x => x.ExternalReference)
            .FirstOrDefaultAsync(ct);

        return MapOrder(order, partnerId, externalRef, fulfillmentStatus);
    }

    public async Task<(IReadOnlyList<PartnerOrderResponse> Items, int Total)> ListAsync(
        string partnerId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);
        page = Math.Clamp(page, 1, 10000);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var baseQuery = db.Orders
            .AsNoTracking()
            .Where(o => o.KeycloakUserId == subject);

        var total = await baseQuery.CountAsync(ct);

        var orders = await baseQuery
            .Include(o => o.Items)
            .OrderByDescending(o => o.CreatedAtUtc)
            .ThenByDescending(o => o.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var ids = orders.Select(o => o.Id).ToArray();

        var fulfillments = await db.FulfillmentResults
            .AsNoTracking()
            .Where(x => ids.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, x => x.Status, ct);

        var externalRefs = await db.PartnerOrderRequests
            .AsNoTracking()
            .Where(x => ids.Contains(x.OrderId))
            .ToDictionaryAsync(x => x.OrderId, x => x.ExternalReference, ct);

        var items = orders.Select(o => MapOrder(
            o,
            partnerId,
            externalRefs.GetValueOrDefault(o.Id),
            fulfillments.GetValueOrDefault(o.Id))).ToList();

        return (items, total);
    }

    private static PartnerOrderResponse MapOrder(
        Order order,
        string partnerId,
        string? externalReference,
        string? fulfillmentStatus)
    {
        var baseResponse = OrderResponse.From(order, fulfillmentStatus);

        return new PartnerOrderResponse(
            order.Id,
            partnerId,
            externalReference,
            order.Status,
            baseResponse.FulfillmentStatus,
            order.TotalAmount,
            "VND",
            order.CreatedAtUtc,
            baseResponse.Items);
    }

    private static string ComputeRequestHash(
        Guid productId,
        int quantity,
        string? externalReference)
    {
        var raw = $"{productId:D}|{quantity}|{externalReference ?? ""}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
