using System.Text.Json;

namespace Defuse.Sources.Stremio;

public sealed record StremioStream(string? Url, string? Title, string? ExternalUrl, IReadOnlyList<string>? Subtitles = null);

public sealed record AddonDescription(string Id, string Name, IReadOnlyList<string> Resources, IReadOnlyList<string> Types);

public static class StremioManifest
{
    public static AddonDescription ParseManifest(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
        var name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "Addon" : "Addon";
        return new AddonDescription(id, name, ReadStrings(root, "resources"), ReadStrings(root, "types"));
    }

    public static IReadOnlyList<StremioStream> ParseStreams(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("streams", out var streams) || streams.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<StremioStream>();
        foreach (var item in streams.EnumerateArray())
        {
            var subtitles = new List<string>();
            if (item.TryGetProperty("subtitles", out var subtitleList) && subtitleList.ValueKind == JsonValueKind.Array)
            {
                foreach (var subtitle in subtitleList.EnumerateArray())
                {
                    if (subtitle.TryGetProperty("url", out var subtitleUrl) && subtitleUrl.GetString() is string subtitleText && subtitleText.Length > 0)
                        subtitles.Add(subtitleText);
                }
            }

            list.Add(new StremioStream(
                item.TryGetProperty("url", out var url) ? url.GetString() : null,
                item.TryGetProperty("title", out var title) ? title.GetString() : null,
                item.TryGetProperty("externalUrl", out var external) ? external.GetString() : null,
                subtitles));
        }

        return list;
    }

    public static Uri ResourceUri(Uri manifestUri, string resource, string type, string id)
    {
        var baseUri = manifestUri.AbsoluteUri;
        var cut = baseUri.LastIndexOf("manifest.json", StringComparison.OrdinalIgnoreCase);
        var prefix = cut >= 0 ? baseUri[..cut] : manifestUri.GetLeftPart(UriPartial.Authority) + "/";
        return new Uri($"{prefix}{resource}/{type}/{Uri.EscapeDataString(id)}.json");
    }

    private static IReadOnlyList<string> ReadStrings(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is string text)
                list.Add(text);
            else if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("name", out var named) && named.GetString() is string resourceName)
                list.Add(resourceName);
        }

        return list;
    }
}
