using System.Text.RegularExpressions;

namespace Defuse.Sources.Library;

public sealed record ParsedName(string Title, string? ShowTitle, int? Season, int? Episode, int? Year, string Type, string? EpisodeTitle = null);

public static partial class FilenameParser
{
    [GeneratedRegex(@"[\\/]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"\bS(\d{1,2})E(\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonEpisode();

    [GeneratedRegex(@"\b(\d{1,2})x(\d{1,3})\b", RegexOptions.IgnoreCase)]
    private static partial Regex CrossEpisode();

    [GeneratedRegex(@"\b(19|20)\d{2}\b")]
    private static partial Regex Year();

    [GeneratedRegex(@"^(?<show>.+?)\s+S\d{1,2}E\d{1,3}\b\s*(?<name>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex SeasonCode();

    public static ParsedName Parse(string path)
    {
        var file = Path.GetFileNameWithoutExtension(path);
        var cleaned = file.Replace('.', ' ').Replace('_', ' ').Trim();
        var seasonEpisode = SeasonEpisode().Match(cleaned);
        var cross = CrossEpisode().Match(cleaned);
        int? season = null;
        int? episode = null;
        var marker = "";
        if (seasonEpisode.Success)
        {
            season = int.Parse(seasonEpisode.Groups[1].Value);
            episode = int.Parse(seasonEpisode.Groups[2].Value);
            marker = seasonEpisode.Value;
        }
        else if (cross.Success)
        {
            season = int.Parse(cross.Groups[1].Value);
            episode = int.Parse(cross.Groups[2].Value);
            marker = cross.Value;
        }

        var yearMatch = Year().Match(cleaned);
        int? year = yearMatch.Success ? int.Parse(yearMatch.Value) : null;
        var titlePart = cleaned;
        if (marker.Length > 0)
            titlePart = cleaned[..cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase)].Trim(' ', '-', '.');
        else if (yearMatch.Success)
            titlePart = cleaned[..yearMatch.Index].Trim(' ', '-', '.');

        if (string.IsNullOrWhiteSpace(titlePart))
            titlePart = cleaned;
        titlePart = CleanTitle(titlePart);

        var type = season is null ? "movie" : "episode";
        var show = season is null ? null : titlePart;
        var episodeTitle = season is null ? null : EpisodeTitleAfter(cleaned, marker);
        var title = season is null || string.IsNullOrWhiteSpace(episodeTitle)
            ? titlePart
            : $"{titlePart} - {episodeTitle}";
        return new ParsedName(title, show, season, episode, year, type, episodeTitle);
    }

    public static string DisplayTitle(string title)
    {
        var match = SeasonCode().Match(title);
        var text = title;
        if (match.Success)
        {
            var show = match.Groups["show"].Value.Trim();
            var name = match.Groups["name"].Value.Trim();
            text = name.Length == 0 ? show : $"{show} - {name}";
        }

        return CleanTitle(text);
    }

    private static string CleanTitle(string value)
    {
        var text = value.Trim();
        while (text.EndsWith('(') || text.EndsWith('[') || text.EndsWith('-') || text.EndsWith('.'))
            text = text[..^1].TrimEnd();
        return text;
    }

    private static string? EpisodeTitleAfter(string cleaned, string marker)
    {
        var index = cleaned.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var words = new List<string>();
        foreach (var word in cleaned[(index + marker.Length)..].Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = word.Trim('-');
            if (token.Length == 0 || IsReleaseTag(token))
                break;
            words.Add(token);
        }

        return words.Count == 0 ? null : string.Join(' ', words);
    }

    private static bool IsReleaseTag(string token)
    {
        var head = token.Split('-', 2)[0];
        return ReleaseTags.Contains(head) || ReleaseTags.Contains(token);
    }

    private static readonly HashSet<string> ReleaseTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "UHD", "HD", "SD", "BluRay", "WEB", "WEBRip", "WEBDL", "HDTV", "HDRip", "DVDRip",
        "REMUX", "HDR", "HDR10", "DV", "DoVi", "HEVC", "AVC", "x265", "x264", "h264", "h265",
        "AAC", "Atmos", "TrueHD", "DTS", "AC3", "EAC3", "HYBRID", "PROPER", "REPACK",
        "2160p", "1080p", "720p", "480p", "4K", "10bit", "8bit", "MULTI", "AMZN", "NF"
    };
}
