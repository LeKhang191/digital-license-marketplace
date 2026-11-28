using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Marketplace.Shared.Security;
using Microsoft.EntityFrameworkCore;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Application.Services;

public static class PartnerWebhookEvents
{
    public const string OrderCompleted = "partner.order.completed";
    public const string OrderRefunded = "partner.order.refunded";
}

public static class PartnerWebhookHelper
{
    private static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web);

    // Enqueues an outbound partner webhook inside the caller's active DB transaction.
    // Payload intentionally contains NO plaintext license keys.
    public static async Task TryEnqueueAsync(
        OrderDbContext db,
        Order order,
        string eventType,
        CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryExtractPartnerIdFromSubject(
                order.KeycloakUserId, out var partnerId))
        {
            return;
        }

        var exists = await db.PartnerWebhookDeliveries
            .AnyAsync(x => x.OrderId == order.Id && x.EventType == eventType, ct);

        if (exists)
            return;

        var externalRef = await db.PartnerOrderRequests
            .AsNoTracking()
            .Where(x => x.OrderId == order.Id)
            .Select(x => x.ExternalReference)
            .SingleOrDefaultAsync(ct);

        var now = DateTime.UtcNow;
        var deliveryId = Guid.NewGuid();

        var payload = JsonSerializer.Serialize(new
        {
            eventId = deliveryId,
            eventType,
            partnerId,
            orderId = order.Id,
            externalReference = externalRef,
            status = order.Status,
            totalAmount = order.TotalAmount,
            currency = "VND",
            claimPath = eventType == PartnerWebhookEvents.OrderCompleted
                ? $"/api/partner/v1/orders/{order.Id:D}/licenses/claim"
                : null,
            occurredAtUtc = now
        }, Json);

        db.PartnerWebhookDeliveries.Add(new PartnerWebhookDelivery
        {
            Id = deliveryId,
            PartnerId = partnerId,
            OrderId = order.Id,
            EventType = eventType,
            Payload = payload,
            Status = "PENDING",
            CreatedAtUtc = now,
            NextRetryAtUtc = now
        });
    }

    public static string ComputeSignature(
        string signingSecret,
        long unixTimestampSeconds,
        string rawPayload)
    {
        var input = $"{unixTimestampSeconds}.{rawPayload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(input));
        return "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<(Uri? Uri, string? Error)> ValidateCallbackUrlAsync(
        string? rawUrl,
        bool allowPrivateNetwork,
        bool allowInsecureHttp,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rawUrl) || rawUrl.Trim().Length > 500)
            return (null, "INVALID_CALLBACK_URL");

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var uri))
            return (null, "INVALID_CALLBACK_URL");

        if (uri.UserInfo.Length > 0 || !string.IsNullOrEmpty(uri.Fragment))
            return (null, "INVALID_CALLBACK_URL");

        if (uri.Scheme == Uri.UriSchemeHttp)
        {
            if (!allowInsecureHttp)
                return (null, "HTTPS_REQUIRED");
        }
        else if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return (null, "INVALID_CALLBACK_SCHEME");
        }

        if (string.IsNullOrWhiteSpace(uri.Host))
            return (null, "INVALID_CALLBACK_URL");

        if (IPAddress.TryParse(uri.Host, out var literalIp))
        {
            if (IsForbiddenAddress(literalIp, allowPrivateNetwork))
                return (null, "CALLBACK_HOST_BLOCKED");

            return (uri, null);
        }

        if (!allowPrivateNetwork &&
            (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)))
        {
            return (null, "CALLBACK_HOST_BLOCKED");
        }

        try
        {
            var addresses = await Dns.GetHostAddressesAsync(uri.Host, ct);
            if (addresses.Length == 0)
                return (null, "CALLBACK_HOST_UNRESOLVED");

            foreach (var addr in addresses)
            {
                if (IsForbiddenAddress(addr, allowPrivateNetwork))
                    return (null, "CALLBACK_HOST_BLOCKED");
            }
        }
        catch (SocketException)
        {
            return (null, "CALLBACK_HOST_UNRESOLVED");
        }

        return (uri, null);
    }

    private static bool IsForbiddenAddress(IPAddress ip, bool allowPrivateNetwork)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
            return true;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = ip.GetAddressBytes();
            // Always block cloud metadata / link-local 169.254.0.0/16 and 0.0.0.0/8.
            if (bytes[0] == 0 || (bytes[0] == 169 && bytes[1] == 254))
                return true;

            if (!allowPrivateNetwork)
            {
                if (IPAddress.IsLoopback(ip) ||
                    bytes[0] == 10 ||
                    (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) ||
                    (bytes[0] == 192 && bytes[1] == 168) ||
                    (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127))
                {
                    return true;
                }
            }

            return false;
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast)
                return true;

            if (!allowPrivateNetwork)
            {
                var bytes = ip.GetAddressBytes();
                var isUniqueLocal = (bytes[0] & 0xFE) == 0xFC;
                if (IPAddress.IsLoopback(ip) || isUniqueLocal || ip.IsIPv6SiteLocal)
                    return true;
            }
        }

        return false;
    }
}
