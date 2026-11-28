using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OrderService.Application.Services;
using OrderService.Domain.Entities;
using OrderService.Infrastructure.Persistence;

namespace OrderService.Controllers;

[ApiController]
[Route("api/partner/v1/webhook")]
[Authorize(Policy = KeycloakAuthentication.PartnerPolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerWebhooksController(
    OrderDbContext db,
    IConfiguration config
) : ControllerBase
{
    public sealed record ConfigureWebhookRequest(
        string CallbackUrl,
        string? SigningSecret,
        bool IsActive = true);

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var endpoint = await db.PartnerWebhookEndpoints
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.PartnerId == partnerId, ct);

        if (endpoint is null)
        {
            return Ok(new
            {
                partnerId,
                configured = false
            });
        }

        return Ok(new
        {
            partnerId,
            configured = true,
            callbackUrl = endpoint.CallbackUrl,
            isActive = endpoint.IsActive,
            hasSigningSecret = !string.IsNullOrEmpty(endpoint.SigningSecret),
            createdAtUtc = endpoint.CreatedAtUtc,
            updatedAtUtc = endpoint.UpdatedAtUtc
        });
    }

    [HttpPut]
    public async Task<IActionResult> Configure(
        [FromBody] ConfigureWebhookRequest request,
        CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var allowPrivate = config.GetValue<bool>("PartnerWebhooks:AllowPrivateNetwork");
        var allowHttp = config.GetValue<bool>("PartnerWebhooks:AllowInsecureHttp");

        var (validatedUri, error) = await PartnerWebhookHelper.ValidateCallbackUrlAsync(
            request.CallbackUrl,
            allowPrivate,
            allowHttp,
            ct);

        if (validatedUri is null)
        {
            return BadRequest(new
            {
                code = error,
                error = "Invalid or disallowed webhook callback URL."
            });
        }

        var endpoint = await db.PartnerWebhookEndpoints
            .SingleOrDefaultAsync(x => x.PartnerId == partnerId, ct);

        var secret = request.SigningSecret?.Trim();
        if (endpoint is null)
        {
            if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32 || secret.Length > 128)
            {
                return BadRequest(new
                {
                    code = "INVALID_SIGNING_SECRET",
                    error = "SigningSecret must be between 32 and 128 characters."
                });
            }

            var now = DateTime.UtcNow;
            endpoint = new PartnerWebhookEndpoint
            {
                PartnerId = partnerId,
                CallbackUrl = validatedUri.AbsoluteUri,
                SigningSecret = secret,
                IsActive = request.IsActive,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            db.PartnerWebhookEndpoints.Add(endpoint);
        }
        else
        {
            if (secret is not null)
            {
                if (secret.Length < 32 || secret.Length > 128)
                {
                    return BadRequest(new
                    {
                        code = "INVALID_SIGNING_SECRET",
                        error = "SigningSecret must be between 32 and 128 characters."
                    });
                }

                endpoint.SigningSecret = secret;
            }

            endpoint.CallbackUrl = validatedUri.AbsoluteUri;
            endpoint.IsActive = request.IsActive;
            endpoint.UpdatedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(ct);

        return Ok(new
        {
            partnerId,
            configured = true,
            callbackUrl = endpoint.CallbackUrl,
            isActive = endpoint.IsActive,
            hasSigningSecret = true,
            updatedAtUtc = endpoint.UpdatedAtUtc
        });
    }

    [HttpGet("deliveries")]
    public async Task<IActionResult> Deliveries(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION",
                error = "Invalid page or page size."
            });
        }

        var query = db.PartnerWebhookDeliveries
            .AsNoTracking()
            .Where(x => x.PartnerId == partnerId);

        var total = await query.CountAsync(ct);

        var items = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(x => new
            {
                deliveryId = x.Id,
                x.OrderId,
                x.EventType,
                x.Status,
                x.AttemptCount,
                x.LastStatusCode,
                x.LastError,
                x.CreatedAtUtc,
                x.NextRetryAtUtc,
                x.DeliveredAtUtc
            })
            .ToListAsync(ct);

        return Ok(new { partnerId, page, pageSize, total, items });
    }
}
