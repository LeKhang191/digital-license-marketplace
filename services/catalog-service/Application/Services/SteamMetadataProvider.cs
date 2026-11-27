using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Catalog.Api.Application.DTOs;

namespace Catalog.Api.Application.Services;

public sealed class SteamMetadataProvider(HttpClient http, IgdbMetadataProvider igdb, IConfiguration config, ILogger<SteamMetadataProvider> log)
{
    public static bool TryAppId(string? input, out int id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(input)) return false;
        input = input.Trim();
        if (int.TryParse(input, out id)) return id > 0;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.Host != "store.steampowered.com") return false;
        var match = Regex.Match(uri.AbsolutePath, @"^/app/([0-9]+)(?:/|$)");
        return match.Success && int.TryParse(match.Groups[1].Value, out id) && id > 0;
    }

    public async Task<SteamMetadata?> FetchAsync(int id, CancellationToken ct)
    {
        if (config["GameMetadata:Provider"]?.Equals("Igdb", StringComparison.OrdinalIgnoreCase) == true)
            return await igdb.FetchAsync(id, ct);
        SteamMetadata? result;
        try { result = await FetchSteamAsync(id, ct); }
        catch (Exception ex) when (igdb.IsConfigured && !ct.IsCancellationRequested &&
            ex is HttpRequestException or JsonException or OperationCanceledException)
        {
            log.LogWarning("Steam request failed ({ErrorType}); trying IGDB", ex.GetType().Name);
            return await igdb.FetchAsync(id, ct);
        }
        return result ?? (igdb.IsConfigured ? await igdb.FetchAsync(id, ct) : null);
    }

    private async Task<SteamMetadata?> FetchSteamAsync(int id, CancellationToken ct)
    {
        using var response = await http.GetAsync($"api/appdetails?appids={id}&l=english&cc=vn", ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var entry = Get(doc.RootElement, id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (Get(entry, "success").ValueKind != JsonValueKind.True) return null;
        var data = Get(entry, "data");
        if (Str(data, "type") != "game" || string.IsNullOrWhiteSpace(Str(data, "name"))) return null;
        var requirements = Get(data, "pc_requirements");
        var trailer = Array(Get(data,"movies")).Select(x => Media(Str(Get(x,"mp4"),"max"))).FirstOrDefault(x => x.Length > 0);
        return new SteamMetadata(id, Plain(Str(data,"name"),160), Plain(Str(data,"short_description"),2000),
            Media(Str(data,"header_image")),
            $"https://cdn.cloudflare.steamstatic.com/steam/apps/{id}/library_600x900.jpg",
            Array(Get(data,"screenshots")).Select(x=>Media(Str(x,"path_full"))).Where(x=>x.Length>0).Take(8).ToArray(),
            Array(Get(data,"genres")).Select(x=>Plain(Str(x,"description"),80)).Take(12).ToArray(),
            Strings(data,"developers"), Strings(data,"publishers"), Plain(Str(Get(data,"release_date"),"date"),100),
            Plain(Str(requirements,"minimum"),8000), Plain(Str(requirements,"recommended"),8000), trailer);
    }
    private static JsonElement Get(JsonElement e,string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key,out var v) ? v : default;
    private static string Str(JsonElement e,string key) => Get(e,key) is var v && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static IEnumerable<JsonElement> Array(JsonElement e) => e.ValueKind == JsonValueKind.Array ? e.EnumerateArray().ToArray() : [];
    private static string[] Strings(JsonElement e,string key) => Array(Get(e,key)).Where(x=>x.ValueKind==JsonValueKind.String).Select(x=>Plain(x.GetString()??"",160)).Take(12).ToArray();
    // React renders these as text, never HTML. Preserve line breaks for requirements.
    private static string Plain(string s,int max)
    {
        s = Regex.Replace(s,@"<(?:br\s*/?|/li|/p|/ul)\s*>","\n",RegexOptions.IgnoreCase);
        s = WebUtility.HtmlDecode(Regex.Replace(s,"<[^>]*>"," ")).Trim();
        return s.Length > max ? s[..max] : s;
    }
    private static string Media(string s)
    {
        if (!Uri.TryCreate(s,UriKind.Absolute,out var u) || u.Scheme != "https" || !u.IsDefaultPort || u.UserInfo.Length != 0) return "";
        var h = u.Host;
        return h.EndsWith(".steamstatic.com",StringComparison.OrdinalIgnoreCase) || h.EndsWith(".akamaihd.net",StringComparison.OrdinalIgnoreCase) && h.StartsWith("steam",StringComparison.OrdinalIgnoreCase) ? u.AbsoluteUri : "";
    }
}
