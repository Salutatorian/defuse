using System.Text.Json;

namespace Defuse.Desktop;

static class CreditMarks
{
    private static Dictionary<Guid, long> _marks = Load();

    public static long? Get(Guid mediaId) =>
        _marks.TryGetValue(mediaId, out var ms) ? ms : null;

    public static void Set(Guid mediaId, long positionMs)
    {
        _marks[mediaId] = Math.Max(0, positionMs);
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "credits.json"), JsonSerializer.Serialize(_marks));
    }

    private static Dictionary<Guid, long> Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "credits.json");
            if (!File.Exists(path))
                return [];
            return JsonSerializer.Deserialize<Dictionary<Guid, long>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
