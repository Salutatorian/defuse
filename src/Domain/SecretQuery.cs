namespace Defuse.Domain;

public static class SecretQuery
{
    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "sig", "signature", "expires", "expiry", "exp",
        "password", "auth", "key"
    };

    public static bool IsSecretKey(string key) =>
        Keys.Contains(key) || key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Key, string Value)> Parse(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return [];
        var text = query.StartsWith('?') ? query[1..] : query;
        var list = new List<(string Key, string Value)>();
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq >= 0 ? part[..eq] : part;
            var rawValue = eq >= 0 ? part[(eq + 1)..] : "";
            list.Add((
                Uri.UnescapeDataString(rawKey.Replace('+', ' ')),
                Uri.UnescapeDataString(rawValue.Replace('+', ' '))));
        }

        return list;
    }
}
