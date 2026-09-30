using System.Text.Json;

namespace Defuse.Desktop;

static class HomeShelf
{
    private static readonly HashSet<string> Hidden = Load();

    public static bool IsHidden(Guid profileId, string token) =>
        Hidden.Contains(Key(profileId, token));

    public static void Hide(Guid profileId, string token)
    {
        if (!Hidden.Add(Key(profileId, token)))
            return;
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "home.json"), JsonSerializer.Serialize(Hidden));
    }

    private static string Key(Guid profileId, string token) => profileId.ToString("D") + "|" + token;

    private static HashSet<string> Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "home.json");
            if (!File.Exists(path))
                return [];
            return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
