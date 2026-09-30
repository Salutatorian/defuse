using System.Net.Http.Headers;
using System.Text.Json;
using System.Xml.Linq;

namespace Defuse.Sources.Servers;

public sealed record RemoteItem(string Id, string Title, string? Path);

public sealed record RemoteSession(string AccessToken, string UserId);

public static class PlaybackUris
{
    public static Uri FtpFile(string host, int port, string path, string? username)
    {
        var builder = new UriBuilder("ftp", host, port, path);
        if (!string.IsNullOrEmpty(username))
            builder.UserName = username;
        return builder.Uri;
    }

    public static Uri JellyfinVideo(Uri server, string itemId, string accessToken) =>
        new($"{server.AbsoluteUri.TrimEnd('/')}/Videos/{itemId}/stream?static=true&api_key={Uri.EscapeDataString(accessToken)}");
}

public sealed class JellyfinClient
{
    private readonly HttpClient _http;

    public JellyfinClient(HttpClient http) => _http = http;

    public async Task<RemoteSession> AuthenticateAsync(Uri server, string username, string password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "/Users/AuthenticateByName"));
        request.Headers.TryAddWithoutValidation("Authorization", "MediaBrowser Client=\"Defuse\", Device=\"PC\", DeviceId=\"defuse\", Version=\"1\"");
        request.Content = new StringContent(JsonSerializer.Serialize(new { Username = username, Pw = password }), System.Text.Encoding.UTF8, "application/json");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var token = doc.RootElement.GetProperty("AccessToken").GetString()
            ?? throw new InvalidOperationException("The server did not return a token.");
        var userId = doc.RootElement.GetProperty("User").GetProperty("Id").GetString()
            ?? throw new InvalidOperationException("The server did not return a user.");
        return new RemoteSession(token, userId);
    }

    public async Task<IReadOnlyList<RemoteItem>> ListAsync(Uri server, string accessToken, string userId, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, $"/Users/{userId}/Items?Recursive=true&IncludeItemTypes=Movie,Episode"));
        request.Headers.Authorization = new AuthenticationHeaderValue("MediaBrowser", $"Token=\"{accessToken}\"");
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var list = new List<RemoteItem>();
        if (!doc.RootElement.TryGetProperty("Items", out var items))
            return list;
        foreach (var item in items.EnumerateArray())
        {
            var id = item.GetProperty("Id").GetString() ?? "";
            var name = item.TryGetProperty("Name", out var nameProp) ? nameProp.GetString() ?? id : id;
            list.Add(new RemoteItem(id, name, null));
        }

        return list;
    }
}

public sealed class WebDavClient
{
    private readonly HttpClient _http;

    public WebDavClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<string>> ListAsync(Uri folder, string? username, string? password, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), folder);
        request.Headers.Add("Depth", "1");
        request.Content = new StringContent("""
            <?xml version="1.0" encoding="utf-8"?>
            <propfind xmlns="DAV:"><prop><displayname/></prop></propfind>
            """, System.Text.Encoding.UTF8, "application/xml");
        if (!string.IsNullOrEmpty(username))
        {
            var raw = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{username}:{password}"));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", raw);
        }

        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var xml = await response.Content.ReadAsStringAsync(cancellationToken);
        var document = XDocument.Parse(xml);
        XNamespace dav = "DAV:";
        return document.Descendants(dav + "href").Select(node => node.Value).Where(value => value.Length > 0).ToArray();
    }
}

public static class NfsMount
{
    public static string Describe(string mountedPath) =>
        Directory.Exists(mountedPath)
            ? "This path is available as a mounted folder."
            : "Mount the NFS share in Windows first, then choose that folder.";
}

public sealed class EmbyClient
{
    private readonly JellyfinClient _jellyfin;

    public EmbyClient(HttpClient http) => _jellyfin = new JellyfinClient(http);

    public Task<RemoteSession> AuthenticateAsync(Uri server, string username, string password, CancellationToken cancellationToken) =>
        _jellyfin.AuthenticateAsync(server, username, password, cancellationToken);

    public Task<IReadOnlyList<RemoteItem>> ListAsync(Uri server, string accessToken, string userId, CancellationToken cancellationToken) =>
        _jellyfin.ListAsync(server, accessToken, userId, cancellationToken);
}

public sealed class PlexClient
{
    private readonly HttpClient _http;

    public PlexClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<RemoteItem>> ListLibrariesAsync(Uri server, string token, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Enter a Plex token.");
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "/library/sections"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.TryAddWithoutValidation("X-Plex-Token", token);
        using var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var list = new List<RemoteItem>();
        if (!doc.RootElement.TryGetProperty("MediaContainer", out var container) || !container.TryGetProperty("Directory", out var directories))
            return list;
        foreach (var item in directories.EnumerateArray())
        {
            var key = item.TryGetProperty("key", out var keyProp) ? keyProp.GetString() ?? "" : "";
            var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? key : key;
            list.Add(new RemoteItem(key, title, null));
        }

        return list;
    }
}
