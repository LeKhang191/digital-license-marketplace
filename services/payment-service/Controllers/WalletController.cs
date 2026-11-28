using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;

namespace PaymentService.Controllers;

public sealed record CreateWalletTopUpRequest(decimal Amount);

[ApiController]
[Route("api/wallet/me")]
[Authorize(Policy = "CustomerOnly")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class WalletController(
    WalletService wallets,
    WalletTopUpService topUps
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

    [HttpPost("topups")]
    public async Task<IActionResult> CreateTopUp(
        [FromBody] CreateWalletTopUpRequest request,
        CancellationToken ct)
    {
        var customerId = CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized();

        var forwarded = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        var clientIp = !string.IsNullOrWhiteSpace(forwarded)
            ? forwarded
            : HttpContext.Connection.RemoteIpAddress?.ToString();

        var (session, error) = await topUps.CreateAsync(
            customerId,
            request.Amount,
            clientIp,
            ct);

        if (session is not null)
            return CreatedAtAction(
                nameof(GetTopUp),
                new { paymentRef = session.PaymentRef },
                session);

        return error switch
        {
            "PAYMENT_NOT_CONFIGURED" or "INVALID_PAYMENT_CONFIGURATION"
                => StatusCode(503, new
                {
                    code = error,
                    error = "Cổng thanh toán VNPay chưa được cấu hình."
                }),
            "INVALID_TOPUP_AMOUNT"
                => BadRequest(new
                {
                    code = error,
                    error = "Số tiền nạp phải là số nguyên từ 10.000 đến 50.000.000 VND."
                }),
            _ => BadRequest(new { code = error ?? "INVALID_REQUEST" })
        };
    }

    [HttpGet("topups/{paymentRef}")]
    public async Task<IActionResult> GetTopUp(
        string paymentRef,
        CancellationToken ct)
    {
        var customerId = CustomerId;
        if (string.IsNullOrWhiteSpace(customerId))
            return Unauthorized();

        var status = await topUps.GetByRefAsync(customerId, paymentRef, ct);
        return status is null ? NotFound() : Ok(status);
    }
}
