using Microsoft.AspNetCore.Mvc;
using LicenseVault.Api.Application.Services;
using LicenseVault.Api.Infrastructure.Security;

namespace LicenseVault.Api.Controllers;

public sealed record FinalizeRefundRequest(
    Guid OrderId,
    Guid RequestId);

[ApiController]
[Route("api/internal/license-refunds")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class InternalLicenseRefundsController(
    InternalServiceAuth auth,
    LicenseRefundService refunds
) : ControllerBase
{
    [HttpPost("prepare")]
    public async Task<IActionResult> Prepare(
        [FromBody] LicenseRefundRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        var (result, error) =
            await refunds.PrepareAsync(request, ct);

        if (result is not null)
            return Ok(result);

        return Conflict(new { code = error });
    }

    [HttpPost("finalize")]
    public async Task<IActionResult> FinalizeRefund(
        [FromBody] FinalizeRefundRequest request,
        CancellationToken ct)
    {
        if (!auth.Validate(Request))
            return Unauthorized();

        var (result, error) =
            await refunds.FinalizeAsync(
                request.OrderId,
                request.RequestId,
                ct);

        if (result is not null)
            return Ok(result);

        return Conflict(new { code = error });
    }
}
