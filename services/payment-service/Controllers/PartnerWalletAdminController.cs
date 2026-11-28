using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/partner-admin/wallets")]
[Authorize(Policy = "TransactionAuditorOnly")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerWalletAdminController(
    WalletService wallets,
    PartnerWalletService partnerWallets
) : ControllerBase
{
    public sealed record TopUpPartnerWalletRequest(
        decimal Amount,
        string? Note);

    [HttpGet("{partnerId}")]
    public async Task<IActionResult> GetWallet(
        string partnerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!KeycloakAuthentication.IsValidPartnerId(partnerId))
        {
            return BadRequest(new
            {
                code = "INVALID_PARTNER_ID",
                error = "Invalid partner ID."
            });
        }

        if (page < 1 || page > 10000 || pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION",
                error = "Invalid page or page size."
            });
        }

        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);
        var balance = await wallets.GetBalanceAsync(subject, ct);
        var topUps = await partnerWallets.ListTopUpsAsync(partnerId, page, pageSize, ct);

        return Ok(new
        {
            partnerId,
            balance = balance.Balance,
            currency = balance.Currency,
            topUps
        });
    }

    [HttpPost("{partnerId}/topups")]
    public async Task<IActionResult> TopUp(
        string partnerId,
        [FromBody] TopUpPartnerWalletRequest request,
        CancellationToken ct)
    {
        var auditorId = User.FindFirst("sub")?.Value;
        if (string.IsNullOrWhiteSpace(auditorId))
            return Unauthorized();

        var idempotencyKey = Request.Headers["Idempotency-Key"].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "IDEMPOTENCY_KEY_REQUIRED",
                error = "Idempotency-Key header is required."
            });
        }

        var (result, error) = await partnerWallets.TopUpAsync(
            partnerId,
            idempotencyKey,
            request.Amount,
            request.Note,
            auditorId,
            ct);

        if (error is not null)
        {
            return error switch
            {
                "IDEMPOTENCY_CONFLICT" => Conflict(new
                {
                    code = error,
                    error = "Idempotency-Key was already used with a different top-up payload."
                }),
                _ => BadRequest(new
                {
                    code = error,
                    error = "Invalid partner wallet top-up request."
                })
            };
        }

        return result!.AlreadyProcessed
            ? Ok(result)
            : StatusCode(201, result);
    }
}
