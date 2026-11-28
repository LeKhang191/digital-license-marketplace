using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/partner/v1/orders/{orderId:guid}/licenses")]
[Authorize(Policy = KeycloakAuthentication.PartnerPolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerLicensesController(
    LicenseDbContext db,
    ILicenseEncryption encryption,
    IHttpClientFactory clients
) : ControllerBase
{
    private sealed record PartnerOrderItem(
        Guid Id,
        Guid ProductId);

    private sealed record PartnerOrderInfo(
        Guid OrderId,
        string PartnerId,
        string Status,
        List<PartnerOrderItem> Items);

    [HttpPost("claim")]
    public async Task<IActionResult> Claim(
        Guid orderId,
        CancellationToken ct)
    {
        if (orderId == Guid.Empty)
            return BadRequest(new { code = "INVALID_ORDER_ID", error = "Invalid order ID." });

        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var owner = KeycloakAuthentication.ToPartnerSubject(partnerId);

        PartnerOrderInfo? order;
        try
        {
            order = await FetchPartnerOrderAsync(orderId, ct);
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            return StatusCode(503, new
            {
                code = "ORDER_SERVICE_UNAVAILABLE",
                error = "Unable to verify partner order status."
            });
        }

        if (order is null ||
            order.OrderId != orderId ||
            !string.Equals(order.PartnerId, partnerId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        if (!string.Equals(order.Status, "COMPLETED", StringComparison.Ordinal))
        {
            return Conflict(new
            {
                code = "ORDER_NOT_COMPLETED",
                status = order.Status,
                error = "Licenses can only be claimed for COMPLETED orders."
            });
        }

        if (order.Items is null || order.Items.Count == 0)
        {
            return StatusCode(503, new
            {
                code = "INVALID_ORDER_DATA",
                error = "Order items could not be verified."
            });
        }

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            await LicenseOrderLock.AcquireAsync(db, orderId, ct);

            if (await db.LicenseRefundGuards.AnyAsync(
                x => x.OrderId == orderId, ct))
            {
                return Conflict(new
                {
                    code = "LICENSE_REFUND_LOCKED",
                    error = "Order is locked by a refund operation."
                });
            }

            var assigned = await db.LicenseKeys
                .FromSqlInterpolated($"""
                    SELECT *
                    FROM license_keys
                    WHERE "OrderId" = {orderId}
                      AND "CustomerId" = {owner}
                      AND "Status" = {LicenseStatus.Assigned}
                    ORDER BY "OrderItemId", "Id"
                    FOR UPDATE
                    """)
                .ToListAsync(ct);

            var expected = order.Items
                .Select(i => (i.Id, i.ProductId))
                .OrderBy(i => i.Id)
                .ToArray();

            var actual = assigned
                .Where(k => k.OrderItemId.HasValue)
                .Select(k => (k.OrderItemId!.Value, k.ProductId))
                .OrderBy(k => k.Value)
                .ToArray();

            if (assigned.Count != expected.Length ||
                !expected.SequenceEqual(actual))
            {
                return Conflict(new
                {
                    code = "LICENSES_NOT_READY",
                    error = "Assigned licenses do not match completed order items."
                });
            }

            var utcNow = DateTime.UtcNow;
            // Align with PostgreSQL timestamptz microsecond precision so the first
            // claim response matches subsequent idempotent reads verbatim.
            var now = new DateTime(utcNow.Ticks - (utcNow.Ticks % 10), DateTimeKind.Utc);
            var payload = new List<object>(assigned.Count);

            foreach (var key in assigned)
            {
                var plaintext = encryption.Decrypt(key.EncryptedKey);
                key.RevealedAtUtc ??= now;

                payload.Add(new
                {
                    licenseId = key.Id,
                    orderItemId = key.OrderItemId!.Value,
                    productId = key.ProductId,
                    licenseKey = plaintext,
                    assignedAtUtc = key.AssignedAtUtc,
                    revealedAtUtc = key.RevealedAtUtc
                });
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            // Never return plaintext before DB commit succeeds.
            return Ok(new
            {
                orderId,
                partnerId,
                licenses = payload
            });
        }
        catch (Exception ex) when (
            ex is System.Security.Cryptography.CryptographicException ||
            ex is FormatException)
        {
            return StatusCode(503, new
            {
                code = "LICENSE_DECRYPT_FAILED",
                error = "Unable to decrypt assigned licenses."
            });
        }
    }

    private async Task<PartnerOrderInfo?> FetchPartnerOrderAsync(
        Guid orderId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"api/partner/v1/orders/{orderId:D}");

        if (!AuthenticationHeaderValue.TryParse(
                Request.Headers.Authorization.ToString(),
                out var auth) ||
            !string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException("Missing bearer token");
        }

        request.Headers.Authorization = auth;

        using var response = await clients
            .CreateClient("LicenseOrders")
            .SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<PartnerOrderInfo>(
            cancellationToken: ct)
            ?? throw new JsonException("Empty partner order response");
    }
}
