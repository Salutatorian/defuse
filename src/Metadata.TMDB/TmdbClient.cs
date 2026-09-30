using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Defuse.Metadata.TMDB;

public sealed record TmdbMatch(int Id, string Title, string? Overview, string? PosterPath, int? Year);

public sealed class TmdbClient
{
    public const string Attribution = "This product uses the TMDB API but is not endorsed or certified by TMDB.";
    private readonly HttpClient _http;

    public TmdbClient(HttpClient http) => _http = http;

    public async Task<IReadOnlyList<TmdbMatch>> SearchMovieAsync(string apiKey, string title, int? year, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(title))
            return [];
        var yearQuery = year is null ? "" : $"&year={year.Value}";
        var uri = $"https://api.themoviedb.org/3/search/movie?api_key={Uri.EscapeDataString(apiKey)}&query={Uri.EscapeDataString(title)}{yearQuery}";
        var payload = await _http.GetFromJsonAsync<SearchResponse>(uri, cancellationToken);
        if (payload?.Results is null)
            return [];
        return payload.Results.Select(item => new TmdbMatch(
            item.Id,
            item.Title ?? item.Name ?? "Untitled",
            item.Overview,
            item.PosterPath,
            YearOf(item.ReleaseDate))).ToArray();
    }

    private static int? YearOf(string? date) =>
        date is { Length: >= 4 } && int.TryParse(date[..4], out var year) ? year : null;

    private sealed class SearchResponse
    {
        [JsonPropertyName("results")]
        public List<Result>? Results { get; set; }
    }

    private sealed class Result
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("title")]
        public string? Title { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("overview")]
        public string? Overview { get; set; }
        [JsonPropertyName("poster_path")]
        public string? PosterPath { get; set; }
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
    }
}
