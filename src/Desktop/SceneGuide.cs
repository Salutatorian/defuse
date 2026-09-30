using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Defuse.Desktop;

internal static class SceneGuide
{
    public readonly record struct Marks(long? IntroEndMs, long? CreditsStartMs);

    public static async Task<Marks> Lookup(HttpClient http, string? showTitle, int? season, int? episode, string title, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        var token = timeout.Token;
        if (season is int seasonNumber && episode is int episodeNumber && !string.IsNullOrWhiteSpace(showTitle))
        {
            var imdb = await FindImdb(http, showTitle, "series", token);
            if (imdb is null)
                return default;
            var payload = await http.GetFromJsonAsync<Segments>(
                $"https://api.introdb.app/segments?imdb_id={Uri.EscapeDataString(imdb)}&season={seasonNumber}&episode={episodeNumber}",
                token);
            return new Marks(payload?.Intro?.EndMs, payload?.Outro?.StartMs);
        }

        if (string.IsNullOrWhiteSpace(title))
            return default;
        var movie = await FindImdb(http, title, "movie", token);
        if (movie is null)
            return default;
        var moviePayload = await http.GetFromJsonAsync<Segments>(
            $"https://api.introdb.app/segments?imdb_id={Uri.EscapeDataString(movie)}&is_movie=true",
            token);
        return new Marks(null, moviePayload?.Outro?.StartMs);
    }

    private static async Task<string?> FindImdb(HttpClient http, string name, string type, CancellationToken cancellationToken)
    {
        var uri = $"https://v3-cinemeta.strem.io/catalog/{type}/top/search={Uri.EscapeDataString(name)}.json";
        var payload = await http.GetFromJsonAsync<Catalog>(uri, cancellationToken);
        var wanted = Fold(name);
        var match = payload?.Metas?.FirstOrDefault(meta =>
            Fold(meta.Name ?? "") == wanted
            && meta.Id is not null
            && meta.Id.StartsWith("tt", StringComparison.Ordinal));
        return match?.Id;
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

    private sealed class Segments
    {
        [JsonPropertyName("intro")]
        public Span? Intro { get; set; }

        [JsonPropertyName("outro")]
        public Span? Outro { get; set; }
    }

    private sealed class Span
    {
        [JsonPropertyName("start_ms")]
        public long? StartMs { get; set; }

        [JsonPropertyName("end_ms")]
        public long? EndMs { get; set; }
    }
}
