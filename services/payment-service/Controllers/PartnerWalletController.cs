using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/partner/v1/wallet")]
[Authorize(Policy = KeycloakAuthentication.PartnerPolicyName)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PartnerWalletController(
    WalletService wallets
) : ControllerBase
{
    [HttpGet("balance")]
    public async Task<IActionResult> GetBalance(CancellationToken ct)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);
        var balance = await wallets.GetBalanceAsync(subject, ct);

        return Ok(new
        {
            partnerId,
            balance = balance.Balance,
            currency = balance.Currency
        });
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        if (!KeycloakAuthentication.TryGetPartnerId(User, out var partnerId))
            return Unauthorized();

        if (page < 1 || page > 10000 ||
            pageSize < 1 || pageSize > 100)
        {
            return BadRequest(new
            {
                code = "INVALID_PAGINATION",
                error = "Invalid page or page size."
            });
        }

        var subject = KeycloakAuthentication.ToPartnerSubject(partnerId);
        var items = await wallets.GetTransactionsAsync(subject, page, pageSize, ct);

        return Ok(new
        {
            partnerId,
            page,
            pageSize,
            items
        });
    }
}
