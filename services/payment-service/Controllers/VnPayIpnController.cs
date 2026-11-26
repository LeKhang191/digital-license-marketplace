using Microsoft.AspNetCore.Mvc;
using PaymentService.Application.Services;

namespace PaymentService.Controllers;

[ApiController]
[Route("api/orders/vnpay-ipn")]
public sealed class VnPayIpnController(
    VnPayIpnHandler handler)
    : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Ipn(
        CancellationToken ct)
    {
        Response.Headers["Cache-Control"] = "no-store";

        if (Request.Query.Any(x => x.Value.Count != 1))
        {
            return Ok(new Dictionary<string, string>
            {
                ["RspCode"] = "97",
                ["Message"] = "Invalid query"
            });
        }

        var query = Request.Query.ToDictionary(
            x => x.Key,
            x => x.Value.ToString(),
            StringComparer.Ordinal);

        var result = await handler.HandleAsync(query, ct);

        return Ok(new Dictionary<string, string>
        {
            ["RspCode"] = result.RspCode,
            ["Message"] = result.Message
        });
    }
}
