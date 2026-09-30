using System.Text.RegularExpressions;

namespace Defuse.Domain;

public static partial class LogScrubber
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Urls();

    [GeneratedRegex(@"(?i)\b(token|sig|signature|password|auth|key)=([^&\s""']+)")]
    private static partial Regex SecretPairs();

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var withoutUrls = Urls().Replace(text, match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ? UrlRedactor.Redact(uri) : "http://…");
        return SecretPairs().Replace(withoutUrls, "$1=…");
    }
}
