using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SupportService.Domain.Entities;

namespace SupportService.Infrastructure.Integrations;

public sealed class JiraClient(IHttpClientFactory clients, IConfiguration config)
{
    public bool Enabled => config.GetValue<bool>("Jira:Enabled");
    private string Setting(string key) => config[$"Jira:{key}"] ?? "";
    public bool Ready => Enabled && Uri.TryCreate(Setting("BaseUrl"), UriKind.Absolute, out var uri)
        && uri.Scheme == "https" && uri.Host.EndsWith(".atlassian.net", StringComparison.OrdinalIgnoreCase)
        && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.AbsolutePath == "/"
        && uri.Query.Length == 0 && uri.Fragment.Length == 0
        && new[] { "Email", "ApiToken" }.All(k => !string.IsNullOrWhiteSpace(Setting(k)))
        && new[] { "ServiceDeskId", "RequestTypeId" }.All(k => int.TryParse(Setting(k), out var n) && n > 0);

    public static string Marker(Guid id) => $"[DLM:{id:D}]";
    private async Task<JsonElement> Call(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!Ready) throw new InvalidOperationException("Jira configuration unavailable");
        using var client = clients.CreateClient("Jira");
        using var req = new HttpRequestMessage(method, Setting("BaseUrl").TrimEnd('/') + "/rest/servicedeskapi/" + path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(
            Encoding.UTF8.GetBytes(Setting("Email") + ":" + Setting("ApiToken"))));
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) req.Content = JsonContent.Create(body);
        using var response = await client.SendAsync(req, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.Clone();
    }
    public Task<JsonElement> Create(SupportTicket ticket, CancellationToken ct) => Call(HttpMethod.Post, "request", new
    {
        serviceDeskId = Setting("ServiceDeskId"), requestTypeId = Setting("RequestTypeId"),
        requestFieldValues = new
        {
            summary = Marker(ticket.Id) + " " + ticket.Category,
            description = $"Marketplace escalation. Ticket: {ticket.Id:D}\nCategory: {ticket.Category}\nOrder reference: {(ticket.OrderId.HasValue ? ticket.OrderId.Value.ToString("D") : "N/A")}\nContact marketplace staff for sanitized details."
        }
    }, ct);

    public Task<JsonElement> Read(string key, CancellationToken ct) => Call(HttpMethod.Get, "request/" + Uri.EscapeDataString(key), null, ct);
    public bool Matches(JsonElement data, Guid ticketId) =>
        data.GetProperty("serviceDeskId").ToString() == Setting("ServiceDeskId") &&
        data.GetProperty("requestTypeId").ToString() == Setting("RequestTypeId") &&
        data.GetProperty("requestFieldValues").EnumerateArray().Any(f =>
            f.GetProperty("fieldId").GetString() == "summary" &&
            (f.GetProperty("value").GetString() ?? "").StartsWith(Marker(ticketId) + " ", StringComparison.Ordinal));

    public async Task<string> Comments(string key, CancellationToken ct)
    {
        var result = new Dictionary<string, object>();
        var start = 0;
        for (var page = 0; page < 20; page++)
        {
            var data = await Call(HttpMethod.Get, $"request/{Uri.EscapeDataString(key)}/comment?public=true&internal=false&start={start}&limit=50", null, ct);
            var values = data.GetProperty("values");
            foreach (var item in values.EnumerateArray())
            {
                if (!item.GetProperty("public").GetBoolean()) continue;
                var id = item.GetProperty("id").ToString();
                var text = item.GetProperty("body").GetString() ?? "";
                result[id] = new { id, body = text.Length > 3000 ? text[..3000] : text };
            }
            if (data.GetProperty("isLastPage").GetBoolean()) return JsonSerializer.Serialize(result.Values);
            if (values.GetArrayLength() == 0) throw new JsonException("Invalid Jira pagination");
            start += values.GetArrayLength();
        }
        throw new JsonException("Jira comment page limit exceeded");
    }
}
