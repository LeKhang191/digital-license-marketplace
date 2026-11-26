using Marketplace.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;
using PaymentService.Infrastructure.Security;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/internal/payments")]
public sealed class InternalPaymentsController(
    InternalServiceAuth auth,
    PaymentSessionService sessions,
    WalletPaymentService walletPayments
) : ControllerBase
{
    [HttpPost("session")]
    public async Task<IActionResult> CreateSession(
        [FromBody] PaymentSessionRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        Response.Headers["Cache-Control"] = "no-store";

        var (session, error) =
            await sessions.CreateAsync(request, ct);

        if (session is not null)
            return Ok(session);

        return error switch
        {
            "PAYMENT_NOT_CONFIGURED" =>
                StatusCode(503, new { code = error }),

            "INVALID_PAYMENT_CONFIGURATION" =>
                StatusCode(503, new { code = error }),

            "PAYMENT_EXPIRED" =>
                Conflict(new { code = error }),

            "PAYMENT_DATA_CONFLICT" =>
                Conflict(new { code = error }),

            "PAYMENT_NOT_PAYABLE" =>
                Conflict(new { code = error }),

            _ => BadRequest(new { code = error })
        };
    }

    [HttpPost("wallet/status")]
    public async Task<IActionResult> WalletStatus(
        [FromBody] PaymentSessionRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        Response.Headers["Cache-Control"] = "no-store";

        var (payment, error) =
            await walletPayments.FindExistingAsync(
                request, ct);

        if (payment is not null)
            return Ok(payment);

        return error == "PAYMENT_NOT_FOUND"
            ? NotFound(new { code = error })
            : Conflict(new { code = error });
    }

    [HttpPost("wallet")]
    public async Task<IActionResult> PayWithWallet(
        [FromBody] PaymentSessionRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        Response.Headers["Cache-Control"] = "no-store";

        var (payment, error) =
            await walletPayments.PayAsync(request, ct);

        if (payment is not null)
            return Ok(payment);

        return error switch
        {
            "INSUFFICIENT_WALLET_BALANCE" or
            "PAYMENT_EXPIRED" or
            "PAYMENT_DATA_CONFLICT" or
            "PAYMENT_NOT_PAYABLE" =>
                Conflict(new { code = error }),

            _ => BadRequest(new { code = error })
        };
    }
}
