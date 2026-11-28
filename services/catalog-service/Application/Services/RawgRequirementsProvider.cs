using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Catalog.Api.Application.Services;

public sealed class RawgRequirementsProvider(HttpClient http, IConfiguration config, ILogger<RawgRequirementsProvider> log)
{
    private string ApiKey => config["Rawg:ApiKey"] ?? "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);

    public async Task<(string Minimum, string Recommended)?> FetchPcRequirementsAsync(string gameName, CancellationToken ct)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(gameName)) return null;

        try
        {
            var searchUrl = $"https://api.rawg.io/api/games?key={Uri.EscapeDataString(ApiKey)}&search={Uri.EscapeDataString(gameName.Trim())}&platforms=4&page_size=5";
            using var searchRes = await http.GetAsync(searchUrl, ct);
            if (!searchRes.IsSuccessStatusCode) return null;

            using var searchDoc = JsonDocument.Parse(await searchRes.Content.ReadAsStringAsync(ct));
            if (!searchDoc.RootElement.TryGetProperty("results", out var results) || results.ValueKind != JsonValueKind.Array)
                return null;

            var normalizedTarget = NormalizeName(gameName);
            long? bestId = null;
            foreach (var item in results.EnumerateArray())
            {
                if (!item.TryGetProperty("id", out var idEl) || idEl.ValueKind != JsonValueKind.Number) continue;
                var candidateName = item.TryGetProperty("name", out var nEl) && nEl.ValueKind == JsonValueKind.String ? nEl.GetString() ?? "" : "";
                var normalizedCandidate = NormalizeName(candidateName);
                if (normalizedCandidate == normalizedTarget)
                {
                    bestId = idEl.GetInt64();
                    break;
                }
                bestId ??= idEl.GetInt64();
            }

            if (bestId is null) return null;

            var detailUrl = $"https://api.rawg.io/api/games/{bestId.Value}?key={Uri.EscapeDataString(ApiKey)}";
            using var detailRes = await http.GetAsync(detailUrl, ct);
            if (!detailRes.IsSuccessStatusCode) return null;

            using var detailDoc = JsonDocument.Parse(await detailRes.Content.ReadAsStringAsync(ct));
            if (!detailDoc.RootElement.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var entry in platforms.EnumerateArray())
            {
                if (!entry.TryGetProperty("platform", out var platform) || platform.ValueKind != JsonValueKind.Object) continue;
                if (!platform.TryGetProperty("id", out var pid) || pid.ValueKind != JsonValueKind.Number || pid.GetInt32() != 4) continue;

                JsonElement req = default;
                if (entry.TryGetProperty("requirements", out var r1) && r1.ValueKind == JsonValueKind.Object) req = r1;
                else if (entry.TryGetProperty("requirements_en", out var r2) && r2.ValueKind == JsonValueKind.Object) req = r2;
                if (req.ValueKind != JsonValueKind.Object) return null;

                var min = Clean(GetStr(req, "minimum"));
                var rec = Clean(GetStr(req, "recommended"));
                if (min.Length == 0 && rec.Length == 0) return null;
                return (min, rec);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            log.LogWarning("RAWG requirements lookup failed for {Game}: {ErrorType}", gameName, ex.GetType().Name);
        }

        return null;
    }

    private static string GetStr(JsonElement e, string key) =>
        e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string NormalizeName(string s) =>
        Regex.Replace(s.ToLowerInvariant(), @"[^a-z0-9]+", "");

    private static string Clean(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Regex.Replace(s, @"<(?:br\s*/?|/li|/p|/ul)\s*>", "\n", RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(Regex.Replace(s, "<[^>]*>", " "));
        var lines = s.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0);
        var text = string.Join("\n", lines);
        return text.Length > 8000 ? text[..8000] : text;
    }
}
