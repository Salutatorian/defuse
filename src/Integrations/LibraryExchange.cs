using System.Text.Json;

namespace Defuse.Integrations;

public sealed record ExportTitle(string Title, string Type, long? PositionMs, bool Completed, string? RedactedSource);

public static class LibraryExchange
{
    public static string ToJson(IReadOnlyList<ExportTitle> titles)
    {
        return JsonSerializer.Serialize(titles, new JsonSerializerOptions { WriteIndented = true });
    }

    public static IReadOnlyList<ExportTitle> FromJson(string json) =>
        JsonSerializer.Deserialize<List<ExportTitle>>(json) ?? [];
}

public static class TraktQueue
{
    public static bool CanSend(string? accessToken) => !string.IsNullOrWhiteSpace(accessToken);

    public static string ScrobbleBody(string title, long positionMs, long durationMs, bool paused)
    {
        var progress = durationMs <= 0 ? 0 : Math.Clamp(positionMs * 100d / durationMs, 0, 100);
        return JsonSerializer.Serialize(new
        {
            movie = new { title },
            progress = Math.Round(progress, 1),
            app_version = "defuse",
            app_date = "2026-09-26"
        }) + (paused ? "" : "");
    }
}

public static class CloudCatalog
{
    public static readonly string[] Providers =
    [
        "Dropbox", "Google Drive", "OneDrive", "Box", "pCloud", "MEGA"
    ];

    public const string NotConnected = "This cloud is not connected. Each provider needs its own sign-in.";
}
