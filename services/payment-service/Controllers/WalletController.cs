using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/wallet/me")]
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class WalletController(
    WalletService wallets
) : ControllerBase
{
    private string? CustomerId =>
        User.FindFirst("sub")?.Value;

    [HttpGet]
    public async Task<IActionResult> GetBalance(
        CancellationToken ct)
    {
        var customerId = CustomerId;

        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized();

        var result = await wallets.GetBalanceAsync(
            customerId, ct);

        return Ok(result);
    }

    [HttpGet("transactions")]
    public async Task<IActionResult> GetTransactions(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var customerId = CustomerId;

        if (string.IsNullOrWhiteSpace(customerId))
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

        var result = await wallets.GetTransactionsAsync(
            customerId,
            page,
            pageSize,
            ct);

        return Ok(result);
    }
}
