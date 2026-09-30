using System.Text.Json;

namespace Defuse.Desktop;

static class WatchQueue
{
    public static IReadOnlyList<Guid> Ids { get; private set; } = Load();

    public static void Replace(IReadOnlyList<Guid> ids)
    {
        Ids = ids.ToArray();
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "queue.json"), JsonSerializer.Serialize(Ids));
    }

    public static Guid? Next(Guid current)
    {
        var index = -1;
        for (var i = 0; i < Ids.Count; i++)
        {
            if (Ids[i] == current)
                index = i;
        }

        if (index < 0 || index + 1 >= Ids.Count)
            return null;
        return Ids[index + 1];
    }

    private static IReadOnlyList<Guid> Load()
    {
        try
        {
            var path = Path.Combine(AppPaths.Root, "queue.json");
            if (!File.Exists(path))
                return [];
            return JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
