namespace Defuse.Domain;

public static class UrlRedactor
{
    public static string Redact(Uri uri)
    {
        var port = uri.IsDefaultPort ? -1 : uri.Port;
        var safe = new UriBuilder(uri.Scheme, uri.Host, port, SecretPath(uri.AbsolutePath)).Uri.AbsoluteUri;
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            return string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) ? safe : safe + "?…";
        return safe;
    }

    private static string SecretPath(string absolutePath)
    {
        var parts = absolutePath.Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0)
                continue;
            if (IsSecretSegment(Uri.UnescapeDataString(parts[i])))
                parts[i] = "…";
        }

        return string.Join('/', parts);
    }

    private static bool IsSecretSegment(string text)
    {
        if (text.Length < 20)
            return false;
        foreach (var character in text)
        {
            if (!char.IsAsciiLetterOrDigit(character))
                return false;
        }

        return true;
    }
}
