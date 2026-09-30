using System.Text.Json;
using Defuse.Application;
using Defuse.Domain;
using Defuse.Persistence;
using Defuse.Playback.Cross;

namespace Defuse.Shell;

public sealed class PlayerSession : IDisposable
{
    private readonly string _queuePath;
    private bool _allowAdvance = true;

    private PlayerSession(string root, LibVlcEngine engine, SqliteLibraryStore store)
    {
        Root = root;
        Engine = engine;
        Store = store;
        Library = new LibraryController(store, Path.Combine(root, "artwork"));
        Playback = new PlaybackCoordinator(engine, store);
        _queuePath = Path.Combine(root, "queue.json");
        Queue = LoadQueue();
        Engine.SnapshotChanged += (_, snapshot) => Playback.OnSnapshot(snapshot);
        Engine.Ended += (_, _) =>
        {
            Playback.Flush();
            if (!_allowAdvance)
                return;
            if (Next() is Guid id)
            {
                _allowAdvance = false;
                Play(id);
            }
        };
    }

    public string Root { get; }
    public LibVlcEngine Engine { get; }
    public SqliteLibraryStore Store { get; }
    public LibraryController Library { get; }
    public PlaybackCoordinator Playback { get; }
    public IReadOnlyList<Guid> Queue { get; private set; }

    public static PlayerSession Open(IUiMarshal ui, string root, string? nativeDirectory)
    {
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "artwork"));
        var protector = new FileSecretProtector(Path.Combine(root, "secret.key"));
        var store = new SqliteLibraryStore(Path.Combine(root, "library.db"), protector);
        store.Initialize();
        var engine = new LibVlcEngine(ui, nativeDirectory ?? LibVlcEngine.FindNativeDirectory());
        return new PlayerSession(root, engine, store);
    }

    public IReadOnlyList<Guid> SaveLinks(string text)
    {
        var lines = text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var ids = new List<Guid>();
        foreach (var line in lines)
        {
            if (line.Length == 0)
                continue;
            ids.Add(Library.Save(Library.FromUrl(line)).MediaId);
        }

        Queue = ids;
        Directory.CreateDirectory(Root);
        File.WriteAllText(_queuePath, JsonSerializer.Serialize(ids));
        return ids;
    }

    public void Play(Guid mediaId)
    {
        var target = Library.Open(mediaId);
        if (target is null)
            return;
        _allowAdvance = true;
        var decision = Playback.Decide(target);
        var start = decision.Kind switch
        {
            ResumeKind.ResumeQuiet or ResumeKind.OfferResume => decision.PositionMs,
            ResumeKind.FromStart or ResumeKind.Completed or ResumeKind.Unseekable => 0,
            _ => 0
        };
        Playback.Play(target, start, true);
    }

    public void PlayOnce(string raw)
    {
        var target = Library.CreateTransient(raw, null, true);
        _allowAdvance = false;
        Playback.Play(target, 0, false);
    }

    public void Replace(Guid mediaId, string raw)
    {
        var target = Library.Replace(mediaId, raw);
        var position = target.Progress?.PositionMs ?? 0;
        Playback.Play(target, position, true);
    }

    public Guid? Next()
    {
        var current = Playback.Current?.MediaId;
        if (current is null)
            return Queue.Count > 0 ? Queue[0] : null;
        var index = -1;
        for (var i = 0; i < Queue.Count; i++)
        {
            if (Queue[i] == current)
                index = i;
        }

        if (index < 0 || index + 1 >= Queue.Count)
            return null;
        return Queue[index + 1];
    }

    public void Stop()
    {
        try
        {
            Playback.Stop();
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void Dispose()
    {
        Stop();
        Engine.Dispose();
        Store.Dispose();
    }

    private IReadOnlyList<Guid> LoadQueue()
    {
        try
        {
            if (!File.Exists(_queuePath))
                return [];
            return JsonSerializer.Deserialize<List<Guid>>(File.ReadAllText(_queuePath)) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }
}
