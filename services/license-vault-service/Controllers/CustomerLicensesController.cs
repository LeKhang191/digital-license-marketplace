using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using LicenseVault.Api.Application.Interfaces;
using LicenseVault.Api.Domain.Entities;
using LicenseVault.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LicenseVault.Api.Controllers;

[ApiController]
[Route("api/licenses/me")]
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CustomerLicensesController(
    LicenseDbContext db,
    ILicenseEncryption encryption,
    IHttpClientFactory clients
) : ControllerBase
{
    private sealed record OrderItem(Guid Id, Guid ProductId);

    private sealed record OrderInfo(
        Guid Id,
        string Status,
        List<OrderItem> Items
    );

    private IQueryable<LicenseKey> Owned(string customer)
        => db.LicenseKeys.AsNoTracking().Where(k =>
            k.CustomerId == customer &&
            k.Status == LicenseStatus.Assigned &&
            k.OrderId != null &&
            k.OrderItemId != null &&
            k.AssignedAtUtc != null);

    private static bool Eligible(
        OrderInfo? order,
        Guid? orderId,
        Guid? itemId,
        Guid productId)
        => order is not null &&
           order.Id == orderId &&
           order.Status == "COMPLETED" &&
           order.Items is not null &&
           order.Items.Any(i =>
               i.Id == itemId &&
               i.ProductId == productId);

    private async Task<T?> Fetch<T>(
        string path,
        CancellationToken ct
    ) where T : class
    {
        using var request =
            new HttpRequestMessage(HttpMethod.Get, path);

        if (!AuthenticationHeaderValue.TryParse(
            Request.Headers.Authorization.ToString(),
            out var auth) ||
            !string.Equals(
                auth.Scheme,
                "Bearer",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new HttpRequestException(
                "Missing bearer token");
        }

        request.Headers.Authorization = auth;

        using var response = await clients
            .CreateClient("LicenseOrders")
            .SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

        response.EnsureSuccessStatusCode();

        return await response.Content
            .ReadFromJsonAsync<T>(cancellationToken: ct)
            ?? throw new JsonException("Empty order response");
    }

    private IActionResult Unavailable()
        => StatusCode(503, new
        {
            error = "Tạm thời không thể xác minh đơn hàng. Vui lòng thử lại."
        });

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] Guid? orderId,
        CancellationToken ct)
    {
        var customer = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(customer))
            return Unauthorized();

        try
        {
            var query = Owned(customer);

            if (orderId.HasValue)
                query = query.Where(k => k.OrderId == orderId);

            var rows = await query
                .OrderByDescending(k => k.AssignedAtUtc)
                .Select(k => new
                {
                    k.Id,
                    k.ProductId,
                    k.OrderId,
                    k.OrderItemId,
                    k.Status,
                    k.AssignedAtUtc
                })
                .ToListAsync(ct);

            if (rows.Count == 0)
                return Ok(rows);

            var orders = await Fetch<List<OrderInfo>>(
                "api/orders", ct) ?? [];

            var byId = orders.ToDictionary(o => o.Id);

            return Ok(rows.Where(k =>
                byId.TryGetValue(k.OrderId!.Value, out var order) &&
                Eligible(order, k.OrderId, k.OrderItemId, k.ProductId))
                .ToList());
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return Unavailable();
        }
    }

    [HttpGet("{id:guid}/reveal")]
    public async Task<IActionResult> Reveal(
        Guid id,
        CancellationToken ct)
    {
        var customer = User.FindFirst("sub")?.Value;

        if (string.IsNullOrWhiteSpace(customer))
            return Unauthorized();

        var key = await Owned(customer)
            .SingleOrDefaultAsync(k => k.Id == id, ct);

        if (key is null)
            return NotFound();

        try
        {
            var order = await Fetch<OrderInfo>(
                $"api/orders/{key.OrderId}", ct);

            if (!Eligible(
                order,
                key.OrderId,
                key.OrderItemId,
                key.ProductId))
                return NotFound();

            var current = await Owned(customer)
                .SingleOrDefaultAsync(k =>
                    k.Id == id &&
                    k.OrderId == key.OrderId &&
                    k.OrderItemId == key.OrderItemId &&
                    k.ProductId == key.ProductId, ct);

            if (current is null)
                return NotFound();

            return Ok(new
            {
                id,
                licenseKey = encryption.Decrypt(
                    current.EncryptedKey)
            });
        }
        catch (Exception ex) when (
            ex is HttpRequestException or JsonException ||
            ex is System.Security.Cryptography.CryptographicException ||
            ex is FormatException ||
            (ex is OperationCanceledException &&
             !ct.IsCancellationRequested))
        {
            return Unavailable();
        }
    }
}
