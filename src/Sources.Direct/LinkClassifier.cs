using Defuse.Domain;

namespace Defuse.Sources.Direct;

public static class LinkClassifier
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".webm", ".ts", ".mov", ".qt", ".avi", ".wmv", ".asf",
        ".flv", ".f4v", ".mpg", ".mpeg", ".mpe", ".mpv", ".m2v", ".m2ts", ".mts", ".m2t",
        ".ogv", ".ogm", ".vob", ".divx", ".3gp", ".3g2", ".mxf", ".wtv", ".dvr-ms",
        ".rm", ".rmvb", ".tod", ".mod", ".evo", ".nsv"
    };

    private static readonly string[] PageHosts =
    {
        "netflix.com", "disneyplus.com", "primevideo.com", "amazon.com",
        "tv.apple.com", "max.com", "hulu.com", "youtube.com", "youtu.be"
    };

    public static ClassifiedLink Classify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Refuse(LinkClass.Unsupported, "Enter a link.");

        var text = input.Trim().Trim('"');
        if (text.StartsWith("stremio:", StringComparison.OrdinalIgnoreCase))
            return Refuse(LinkClass.StremioDeepLink, "This is a Stremio app link, not a video file.");
        if (text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return Refuse(LinkClass.Magnet, "Torrent links are not playable in this release.");
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
            return new ClassifiedLink(LinkClass.UncPath, null, text, false, false, false, false,
                "Network folders can be added under Settings, Connections.", Path.GetFileName(text.TrimEnd('\\')));

        if (LooksLikeLocalPath(text))
        {
            if (text.EndsWith('\\') || text.EndsWith('/'))
                return new ClassifiedLink(LinkClass.LocalFolder, null, text, false, false, false, false,
                    "This folder can be scanned into the library.", text);
            var ext = Path.GetExtension(text);
            if (ext.Equals(".strm", StringComparison.OrdinalIgnoreCase) || ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                return Refuse(LinkClass.PlaylistFile, "Playlists and .strm files can be imported from a folder scan.");
            if (!VideoExtensions.Contains(ext))
                return Refuse(LinkClass.Unsupported, "That local file is not a video type this player opens.");
            return new ClassifiedLink(LinkClass.LocalFile, null, text, true, false, false, false, "", Path.GetFileName(text));
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return Refuse(LinkClass.Unsupported, "This does not look like a playable video link.");
        if (uri.Scheme == Uri.UriSchemeFile)
            return Classify(uri.LocalPath);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Refuse(LinkClass.Unsupported, "This link type is not playable in this release.");

        var loopback = IsLoopback(uri);
        var stremio = loopback && uri.Port == 11470;
        var mayExpire = SecretQuery.Parse(uri.Query).Any(pair => SecretQuery.IsSecretKey(pair.Key));
        var redacted = UrlRedactor.Redact(uri);
        var fileName = Path.GetFileName(uri.AbsolutePath);
        var extName = Path.GetExtension(uri.AbsolutePath);

        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            return new ClassifiedLink(LinkClass.AddonManifest, uri, null, false, false, loopback, stremio,
                "This is an addon link. Add it under Settings, Connections.", redacted);
        if (extName.Equals(".m3u", StringComparison.OrdinalIgnoreCase) || extName.Equals(".strm", StringComparison.OrdinalIgnoreCase))
            return new ClassifiedLink(LinkClass.PlaylistFile, uri, null, false, mayExpire, loopback, stremio,
                "Playlists and .strm files are imported as a list, not sent to the player.", redacted);

        var kind = KindForExtension(extName, loopback);
        if (kind is not null)
            return new ClassifiedLink(kind.Value, uri, null, true, mayExpire, loopback, stremio,
                PlayableMessage(loopback, stremio, mayExpire), redacted);

        if (IsPageHost(uri.Host))
            return new ClassifiedLink(LinkClass.ExternalPage, uri, null, false, false, loopback, stremio,
                "This is a web page. You can save a shortcut. This player will not play it.", redacted);

        return new ClassifiedLink(LinkClass.UnknownHttp, uri, null, false, mayExpire, loopback, stremio,
            "No video file type was detected. You can try to play it, or save a shortcut.", redacted);
    }

    private static LinkClass? KindForExtension(string ext, bool loopback)
    {
        if (ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
            return LinkClass.Hls;
        if (ext.Equals(".mpd", StringComparison.OrdinalIgnoreCase))
            return LinkClass.Dash;
        if (VideoExtensions.Contains(ext))
            return loopback ? LinkClass.LoopbackDirect : LinkClass.DirectFile;
        return null;
    }

    private static string PlayableMessage(bool loopback, bool stremio, bool mayExpire)
    {
        if (stremio)
            return "This plays only while Stremio is open on this PC.";
        if (loopback)
            return "This plays only while the program serving it on this PC is running.";
        if (mayExpire)
            return "This link may expire. The title and your place are saved separately.";
        return "";
    }

    private static bool IsPageHost(string host)
    {
        foreach (var page in PageHosts)
        {
            if (host.Equals(page, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + page, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || uri.Host.Equals("127.0.0.1", StringComparison.Ordinal)
        || uri.Host.Equals("::1", StringComparison.Ordinal);

    private static bool LooksLikeLocalPath(string text) =>
        text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/');

    private static ClassifiedLink Refuse(LinkClass kind, string message) =>
        new(kind, null, null, false, false, false, false, message, "");
}
