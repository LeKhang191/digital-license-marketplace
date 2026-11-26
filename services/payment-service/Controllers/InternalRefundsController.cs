using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Security;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/internal/refunds")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class InternalRefundsController(
    InternalServiceAuth auth,
    RefundService refunds
) : ControllerBase
{
    [HttpPost("wallet")]
    public async Task<IActionResult> RefundToWallet(
        [FromBody] WalletRefundRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        var (refund, error) =
            await refunds.RefundToWalletAsync(request, ct);

        if (refund is not null)
            return Ok(refund);

        return error switch
        {
            "INVALID_REFUND_REQUEST" =>
                BadRequest(new { code = error }),

            "PAYMENT_NOT_FOUND" =>
                NotFound(new { code = error }),

            _ => Conflict(new { code = error })
        };
    }
}
