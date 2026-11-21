using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Catalog.Api.Infrastructure.Security;

public sealed class AdminApiKeyFilter(
    IConfiguration configuration
) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(
        ActionExecutingContext context,
        ActionExecutionDelegate next)
    {
        var expected = configuration["ADMIN_API_KEY"];

        if (string.IsNullOrWhiteSpace(expected))
        {
            context.Result = new StatusCodeResult(503);
            return;
        }

        var supplied = context.HttpContext.Request
            .Headers["X-Admin-Key"].ToString();

        if (string.IsNullOrEmpty(supplied) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expected),
                Encoding.UTF8.GetBytes(supplied)))
        {
            context.Result = new UnauthorizedObjectResult(
                new { error = "Invalid admin API key" }
            );
            return;
        }

        await next();
    }
}