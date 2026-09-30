namespace Defuse.Sources.Library;

public static class FolderScanner
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".webm", ".ts", ".mov", ".qt", ".avi", ".wmv", ".asf",
        ".flv", ".f4v", ".mpg", ".mpeg", ".mpe", ".mpv", ".m2v", ".m2ts", ".mts", ".m2t",
        ".ogv", ".ogm", ".vob", ".divx", ".3gp", ".3g2", ".mxf", ".wtv", ".dvr-ms",
        ".rm", ".rmvb", ".tod", ".mod", ".evo", ".nsv"
    };

    public static IReadOnlyList<string> VideoFiles(string root)
    {
        if (!Directory.Exists(root))
            return [];
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
