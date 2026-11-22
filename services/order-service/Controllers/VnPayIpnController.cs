using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderService.Application.Services;

namespace OrderService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/orders/vnpay-ipn")]
public sealed class VnPayIpnController(
    VnPayIpnHandler handler
) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Ipn(CancellationToken ct)
    {
        var query = Request.Query.ToDictionary(
            kv => kv.Key, kv => kv.Value.ToString());

        var result = await handler.HandleAsync(query, ct);

        // VNPay yêu cầu đúng tên trường "RspCode" và "Message"
        // (không để JSON tự đổi thành camelCase)
        return Content(
            $"{{\"RspCode\":\"{result.RspCode}\"," +
            $"\"Message\":\"{result.Message}\"}}",
            "application/json");
    }
}
