using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SupportService.Domain.Entities;
using SupportService.Infrastructure.Integrations;
using SupportService.Infrastructure.Persistence;

namespace SupportService.Controllers;

[ApiController]
[Authorize(Roles = "license_coordinator,transaction_auditor,catalog_manager")]
[Route("api/support/tickets/{id:guid}")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class SupportExternalController(SupportDbContext db, JiraClient jira) : ControllerBase
{
    private bool Assigned(SupportTicket? t) => t is not null &&
        !string.IsNullOrWhiteSpace(User.FindFirst("sub")?.Value) &&
        t.HandledBy == User.FindFirst("sub")?.Value && User.IsInRole(t.AssignedRole);
    private static object View(ExternalSupportLink link) => new
    {
        link.TicketId, link.State, link.IssueId, link.IssueKey, link.ExternalStatus,
        comments = JsonSerializer.Deserialize<JsonElement>(link.CommentsJson), link.SyncedAtUtc
    };
    internal static bool RemoteFailure(Exception ex) => ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException;
    private IActionResult Unavailable() => StatusCode(503, new { code = "JIRA_UNAVAILABLE" });
    internal static void Attach(ExternalSupportLink link, JsonElement data)
    {
        var issueId = data.GetProperty("issueId").ToString();
        var issueKey = data.GetProperty("issueKey").GetString();
        if (string.IsNullOrWhiteSpace(issueId) || string.IsNullOrWhiteSpace(issueKey)) throw new JsonException("Invalid Jira issue");
        link.IssueId = issueId; link.IssueKey = issueKey;
        link.ExternalStatus = data.GetProperty("currentStatus").GetProperty("status").GetString();
        link.State = "LINKED"; link.Version = Guid.NewGuid();
    }

    [HttpPost("escalate")]
    public async Task<IActionResult> Escalate(Guid id, CancellationToken ct)
    {
        // Persist the intent before the external POST. Never repeat a possibly successful POST.
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            var rows = await db.SupportTickets.FromSqlInterpolated($"SELECT * FROM support_tickets WHERE \"Id\" = {id} FOR UPDATE").ToListAsync(ct);
            var ticket = rows.SingleOrDefault();
            if (!Assigned(ticket)) return NotFound();
            var existing = await db.ExternalSupportLinks.SingleOrDefaultAsync(x => x.TicketId == id, ct);
            if (existing is not null) return existing.State == "LINKED" ? Ok(View(existing)) : Conflict(new { code = "JIRA_RECONCILIATION_REQUIRED", state = existing.State });
            if (ticket!.Status != "IN_PROGRESS") return Conflict(new { code = "TICKET_NOT_IN_PROGRESS" });
            if (!jira.Ready) return Unavailable();
            db.ExternalSupportLinks.Add(new ExternalSupportLink { TicketId = id });
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        var link = await db.ExternalSupportLinks.SingleAsync(x => x.TicketId == id, ct);
        var current = await db.SupportTickets.AsNoTracking().SingleAsync(x => x.Id == id, ct);
        try
        {
            var data = await jira.Create(current, ct);
            Attach(link, data);
            await db.SaveChangesAsync(ct);
            return Ok(View(link));
        }
        catch (Exception ex) when (RemoteFailure(ex))
        {
            // On cancellation/crash PENDING remains durable and also blocks retry.
            link.State = "UNCERTAIN"; link.Version = Guid.NewGuid();
            if (!ct.IsCancellationRequested) await db.SaveChangesAsync(ct);
            return StatusCode(503, new { code = "JIRA_RECONCILIATION_REQUIRED" });
        }
    }

    [HttpGet("external")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!Assigned(await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct))) return NotFound();
        var link = await db.ExternalSupportLinks.AsNoTracking().SingleOrDefaultAsync(x => x.TicketId == id, ct);
        return link is null ? NotFound() : Ok(View(link));
    }

    [HttpPost("external/sync")]
    public async Task<IActionResult> Sync(Guid id, CancellationToken ct)
    {
        if (!Assigned(await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct))) return NotFound();
        var link = await db.ExternalSupportLinks.SingleOrDefaultAsync(x => x.TicketId == id, ct);
        if (link is null) return NotFound();
        if (link.State != "LINKED" || link.IssueId is null) return Conflict(new { code = "JIRA_RECONCILIATION_REQUIRED" });
        if (!jira.Ready) return Unavailable();
        try
        {
            await RefreshLink(db, jira, link, ct);
            return Ok(View(link));
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { code = "JIRA_SYNC_CONFLICT" }); }
        catch (Exception ex) when (RemoteFailure(ex)) { return Unavailable(); }
    }

    internal static async Task RefreshLink(SupportDbContext db, JiraClient jira, ExternalSupportLink link, CancellationToken ct)
    {
        var issueId = link.IssueId ?? throw new InvalidOperationException("Missing Jira issue");
        var data = await jira.Read(issueId, ct);
        var comments = await jira.Comments(issueId, ct);
        if (data.GetProperty("issueId").ToString() != issueId) throw new JsonException("Unexpected issue");
        Attach(link, data);
        link.CommentsJson = comments;
        link.SyncedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    public sealed record ReconcileRequest(string IssueKey);
    [HttpPost("external/reconcile")]
    public async Task<IActionResult> Reconcile(Guid id, ReconcileRequest request, CancellationToken ct)
    {
        if (!Assigned(await db.SupportTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct))) return NotFound();
        var link = await db.ExternalSupportLinks.SingleOrDefaultAsync(x => x.TicketId == id, ct);
        if (link is null) return NotFound();
        if (link.State == "LINKED") return Conflict(new { code = "ALREADY_LINKED" });
        if (DateTime.UtcNow - link.CreatedAtUtc < TimeSpan.FromMinutes(2)) return Conflict(new { code = "JIRA_CREATION_IN_PROGRESS" });
        if (string.IsNullOrWhiteSpace(request.IssueKey) || request.IssueKey.Length > 64) return BadRequest();
        if (!jira.Ready) return Unavailable();
        try
        {
            var data = await jira.Read(request.IssueKey, ct);
            if (!jira.Matches(data, id)) return Conflict(new { code = "JIRA_TICKET_MISMATCH" });
            Attach(link, data);
            await db.SaveChangesAsync(ct);
            return Ok(View(link));
        }
        catch (DbUpdateException) { return Conflict(new { code = "JIRA_LINK_CONFLICT" }); }
        catch (Exception ex) when (RemoteFailure(ex)) { return Unavailable(); }
    }
}
