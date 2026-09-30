using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Defuse.Desktop;

internal static class CoverArt
{
    public static async Task<string?> SavePoster(HttpClient http, string name, string destination, CancellationToken cancellationToken)
    {
        var url = await PosterUrl(http, name, cancellationToken);
        if (url is null)
            return null;
        var bytes = await http.GetByteArrayAsync(url, cancellationToken);
        if (bytes.Length < 32)
            return null;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, bytes, cancellationToken);
        return destination;
    }

    private static async Task<string?> PosterUrl(HttpClient http, string name, CancellationToken cancellationToken)
    {
        foreach (var type in new[] { "series", "movie" })
        {
            var uri = $"https://v3-cinemeta.strem.io/catalog/{type}/top/search={Uri.EscapeDataString(name)}.json";
            var payload = await http.GetFromJsonAsync<Catalog>(uri, cancellationToken);
            var wanted = Fold(name);
            var match = payload?.Metas?.FirstOrDefault(meta =>
                Fold(meta.Name ?? "") == wanted
                && !string.IsNullOrWhiteSpace(meta.Id));
            if (match?.Id is { } id && id.StartsWith("tt", StringComparison.Ordinal))
                return $"https://images.metahub.space/poster/medium/{id}/img";
        }

        return null;
    }

    private static string Fold(string value)
    {
        var letters = new char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
                letters[count++] = char.ToLowerInvariant(character);
        }

        return new string(letters, 0, count);
    }

    private sealed class Catalog
    {
        [JsonPropertyName("metas")]
        public List<Meta>? Metas { get; set; }
    }

    private sealed class Meta
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
