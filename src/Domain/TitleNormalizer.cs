using System.Text.RegularExpressions;

namespace Defuse.Domain;

public static partial class TitleNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string title) =>
        Whitespace().Replace(title.Trim().ToLowerInvariant(), " ");
}
