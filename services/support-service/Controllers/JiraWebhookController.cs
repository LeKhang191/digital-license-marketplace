using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportService.Infrastructure.Integrations;
using SupportService.Infrastructure.Persistence;

namespace SupportService.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/support/webhooks/jira")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class JiraWebhookController(SupportDbContext db, JiraClient jira, IConfiguration config) : ControllerBase
{
    public sealed record JiraEvent(string? IssueId);

    [HttpPost]
    [RequestSizeLimit(4096)]
    public async Task<IActionResult> Receive(JiraEvent request, CancellationToken ct)
    {
        var secret = config["Jira:WebhookSecret"] ?? "";
        if (!jira.Enabled || secret.Length < 32) return StatusCode(503);
        if (!Request.Headers.TryGetValue("X-DLM-Webhook-Secret", out var values) || values.Count != 1 ||
            !CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(secret)),
                SHA256.HashData(Encoding.UTF8.GetBytes(values.ToString()))))
            return Unauthorized();

        var id = request.IssueId;
        if (string.IsNullOrEmpty(id) || id.Length > 32 || id.Any(c => c < '0' || c > '9'))
            return BadRequest();

        // Only existing linked issues may be refreshed; never trust webhook status/comments.
        var link = await db.ExternalSupportLinks.SingleOrDefaultAsync(x => x.IssueId == id && x.State == "LINKED", ct);
        if (link is null) return Ok(new { result = "ignored" });
        if (!jira.Ready) return StatusCode(503);
        try
        {
            await SupportExternalController.RefreshLink(db, jira, link, ct);
            return Ok(new { result = "synced" });
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "JIRA_SYNC_CONFLICT" }); }
        catch (Exception ex) when (SupportExternalController.RemoteFailure(ex))
        { return StatusCode(503, new { code = "JIRA_UNAVAILABLE" }); }
    }
}
