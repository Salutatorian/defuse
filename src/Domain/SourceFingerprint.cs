using System.Security.Cryptography;
using System.Text;

namespace Defuse.Domain;

public static class SourceFingerprint
{
    public static string Compute(Uri uri)
    {
        var kept = SecretQuery.Parse(uri.Query)
            .Where(pair => !SecretQuery.IsSecretKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");
        var canonical = $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}{uri.AbsolutePath}?{string.Join("&", kept)}";
        return Hash(canonical);
    }

    public static string ComputeLocal(string path)
    {
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        return Hash(canonical);
    }

    private static string Hash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
