namespace Defuse.Desktop;

public static class AppPaths
{
    public static string Root { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Defuse");
    public static string Database { get; } = Path.Combine(Root, "library.db");
    public static string Artwork { get; } = Path.Combine(Root, "artwork");
}
