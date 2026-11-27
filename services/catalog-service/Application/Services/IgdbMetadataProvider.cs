using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Catalog.Api.Application.DTOs;
using Microsoft.Extensions.Caching.Memory;

namespace Catalog.Api.Application.Services;

public sealed class IgdbMetadataProvider(HttpClient http, IConfiguration config, IMemoryCache cache)
{
    private string ClientId => config["Igdb:ClientId"] ?? "";
    private string Secret => config["Igdb:ClientSecret"] ?? "";
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(Secret);

    private async Task<string> TokenAsync(CancellationToken ct)
    {
        if (!IsConfigured) throw new HttpRequestException("IGDB credentials missing");
        if (cache.TryGetValue<string>("igdb-app-token", out var cached) && cached is not null) return cached;
        using var content = new FormUrlEncodedContent(new Dictionary<string,string>
        {
            ["client_id"] = ClientId, ["client_secret"] = Secret, ["grant_type"] = "client_credentials"
        });
        using var response = await http.PostAsync("https://id.twitch.tv/oauth2/token", content, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var token = Str(doc.RootElement, "access_token");
        if (token.Length == 0) throw new JsonException("Missing access token");
        var expires = Get(doc.RootElement, "expires_in");
        if (expires.ValueKind == JsonValueKind.Number && expires.TryGetInt32(out var seconds) && seconds > 60)
            cache.Set("igdb-app-token", token, TimeSpan.FromSeconds(seconds - 60));
        return token;
    }

    private async Task<JsonElement> QueryAsync(string endpoint, string query, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/" + endpoint);
        request.Headers.Add("Client-ID", ClientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await TokenAsync(ct));
        request.Content = new StringContent(query, Encoding.UTF8, "text/plain");
        using var response = await http.SendAsync(request, ct);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized) cache.Remove("igdb-app-token");
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new JsonException("Expected IGDB array");
        return doc.RootElement.Clone();
    }

    public async Task<SteamMetadata?> FetchAsync(int appId, CancellationToken ct)
    {
        if (appId <= 0) return null;
        var id = appId.ToString(CultureInfo.InvariantCulture);
        var matches = await QueryAsync("external_games", $"fields game; where external_game_source = 1 & uid = \"{id}\"; limit 50;", ct);
        var ids = Items(matches).Select(x => Get(x,"game")).Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt64()).Distinct().ToArray();
        if (ids.Length != 1) return null; // Never guess a game for an ambiguous external ID.
        var games = await QueryAsync("games", $"fields name,summary,cover.image_id,screenshots.image_id,genres.name,involved_companies.developer,involved_companies.publisher,involved_companies.company.name,first_release_date; where id = {ids[0]}; limit 1;", ct);
        var game = Items(games).FirstOrDefault();
        var name = Trim(Str(game,"name"),160);
        if (string.IsNullOrWhiteSpace(name)) return null;
        var cover = Image(Get(game,"cover"),"t_cover_big");
        var screenshots = Items(Get(game,"screenshots")).Select(x=>Image(x,"t_screenshot_big")).Where(x=>x.Length>0).Take(8).ToArray();
        var header = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/header.jpg";
        var date = Get(game,"first_release_date");
        var release = date.ValueKind == JsonValueKind.Number && date.TryGetInt64(out var unix) && unix >= -62135596800L && unix <= 253402300799L
            ? DateTimeOffset.FromUnixTimeSeconds(unix).ToString("yyyy-MM-dd",CultureInfo.InvariantCulture) : "";
        string[] Companies(string role) => Items(Get(game,"involved_companies"))
            .Where(x=>Get(x,role).ValueKind==JsonValueKind.True).Select(x=>Trim(Str(Get(x,"company"),"name"),160))
            .Where(x=>x.Length>0).Distinct().Take(12).ToArray();
        return new SteamMetadata(appId,name,Trim(Str(game,"summary"),2000),header,cover,screenshots,
            Items(Get(game,"genres")).Select(x=>Trim(Str(x,"name"),80)).Where(x=>x.Length>0).Take(12).ToArray(),
            Companies("developer"),Companies("publisher"),release,"","",null,"IGDB");
    }
    private static JsonElement Get(JsonElement e,string key) => e.ValueKind==JsonValueKind.Object && e.TryGetProperty(key,out var v) ? v : default;
    private static string Str(JsonElement e,string key) => Get(e,key) is var v && v.ValueKind==JsonValueKind.String ? v.GetString() ?? "" : "";
    private static IEnumerable<JsonElement> Items(JsonElement e) => e.ValueKind==JsonValueKind.Array ? e.EnumerateArray().ToArray() : [];
    private static string Trim(string s,int max) => s.Trim().Length > max ? s.Trim()[..max] : s.Trim();
    private static string Image(JsonElement e,string size)
    {
        var id = Str(e,"image_id");
        return Regex.IsMatch(id,@"^[A-Za-z0-9_]{1,100}$") ? $"https://images.igdb.com/igdb/image/upload/{size}/{id}.jpg" : "";
    }
}
