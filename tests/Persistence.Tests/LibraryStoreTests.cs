using Defuse.Application;
using Defuse.Domain;
using Defuse.Persistence;
using Microsoft.Data.Sqlite;

namespace Defuse.Persistence.Tests;

public class LibraryStoreTests
{
    [Fact]
    public void Token_is_absent_from_the_database_file()
    {
        using var fixture = new LibraryFixture();
        var saved = fixture.Controller.Save(Request("https://cdn.example/video.mp4?id=abc&token=SUPERSECRETTOKEN", "Night Drive"));
        fixture.Store.UpsertProgress(Progress(fixture, saved.MediaId, 2_530_000));
        var replaced = fixture.Controller.Replace(saved.MediaId, "https://cdn.example/video.mp4?id=abc&token=ROTATEDSECRET");
        var progress = fixture.Store.GetProgress(fixture.Store.ActiveProfileId, saved.MediaId);
        Assert.Equal(2_530_000, progress!.PositionMs);
        Assert.Equal(replaced.MediaId, saved.MediaId);
        var bytes = ReadDatabase(fixture.DatabasePath);
        var text = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain("SUPERSECRETTOKEN", text);
        Assert.DoesNotContain("ROTATEDSECRET", text);
    }

    [Fact]
    public void Path_key_stays_playable_and_leaves_the_display_copy()
    {
        using var fixture = new LibraryFixture();
        var key = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCD";
        var url = $"https://cdn.example/resolve/{key}/Show.S01E01.mkv";
        var saved = fixture.Controller.Save(Request(url, "Show"));
        var target = fixture.Store.GetPlayTarget(saved.MediaId)!;
        Assert.Equal(url, target.Locator);
        Assert.DoesNotContain(key, target.RedactedLocator);
        Assert.Contains("Show.S01E01.mkv", target.RedactedLocator);
        Assert.DoesNotContain(key, ReadText(fixture.DatabasePath));

        using (var connection = new SqliteConnection($"Data Source={fixture.DatabasePath}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE sources SET locator_redacted = $url;";
            command.Parameters.AddWithValue("$url", url);
            command.ExecuteNonQuery();
        }

        fixture.Store.Initialize();
        var again = fixture.Store.GetPlayTarget(saved.MediaId)!;
        Assert.Equal(url, again.Locator);
        Assert.DoesNotContain(key, again.RedactedLocator);
    }

    [Fact]
    public void Same_fingerprint_updates_one_title_and_manifests_are_refused()
    {
        using var fixture = new LibraryFixture();
        var first = fixture.Controller.Save(Request("https://cdn.example/a.mp4?id=keep&token=one", "One"));
        var second = fixture.Controller.Save(Request("https://cdn.example/a.mp4?id=keep&token=two", "One"));
        Assert.True(second.UpdatedExisting);
        Assert.Equal(first.MediaId, second.MediaId);
        Assert.Single(fixture.Store.ListLibrary());
        var error = Assert.Throws<InvalidOperationException>(() =>
            fixture.Controller.Save(Request("https://addon.example/manifest.json", "Addon")));
        Assert.Contains("Settings, Connections", error.Message);
    }

    [Fact]
    public void Transient_play_inserts_nothing_and_continue_watching_uses_the_window()
    {
        using var fixture = new LibraryFixture();
        var transient = fixture.Controller.CreateTransient("https://cdn.example/clip.mp4", null, false);
        Assert.Equal(Guid.Empty, transient.MediaId);
        Assert.Empty(fixture.Store.ListLibrary());

        var saved = fixture.Controller.Save(Request("https://cdn.example/movie.mp4", "Stored"));
        var target = fixture.Store.GetPlayTarget(saved.MediaId)!;
        fixture.Store.UpsertProgress(new PlaybackProgress(
            fixture.Store.ActiveProfileId, saved.MediaId, target.VersionId, 45_000, 600_000, false, DateTimeOffset.UtcNow, target.SourceId));
        Assert.Single(fixture.Controller.Continue());
        fixture.Controller.MarkWatched(saved.MediaId);
        Assert.Empty(fixture.Controller.Continue());
    }

    [Fact]
    public void Play_writes_the_resume_point_before_the_engine_loads()
    {
        using var fixture = new LibraryFixture();
        var saved = fixture.Controller.Save(Request("https://cdn.example/play.mp4", "Playable"));
        var target = fixture.Store.GetPlayTarget(saved.MediaId)!;
        var engine = new FakeEngine();
        var coordinator = new PlaybackCoordinator(engine, fixture.Store);
        engine.OnLoad = () => Assert.Equal(12_000, fixture.Store.GetProgress(fixture.Store.ActiveProfileId, saved.MediaId)!.PositionMs);
        coordinator.Play(target, 12_000, true);
        coordinator.StopPersisting();
        engine.Current = new PlayerSnapshot(0, 600_000, true, false, true, null);
        coordinator.OnSnapshot(engine.Current);
        Assert.Equal(12_000, fixture.Store.GetProgress(fixture.Store.ActiveProfileId, saved.MediaId)!.PositionMs);
    }

    [Fact]
    public void A_newer_library_is_refused()
    {
        var path = Path.Combine(Path.GetTempPath(), $"defuse-newer-{Guid.NewGuid():N}.db");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99;";
            command.ExecuteNonQuery();
        }

        var store = new SqliteLibraryStore(path, new XorProtector());
        var error = Assert.Throws<InvalidOperationException>(store.Initialize);
        Assert.Contains("newer Defuse", error.Message);
        SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    [Fact]
    public void One_thousand_titles_round_trip()
    {
        using var fixture = new LibraryFixture();
        for (var i = 0; i < 1000; i++)
            fixture.Store.CreateTitle(Title($"Title {i}", $"https://cdn.example/list/{i}.mp4"));
        Assert.Equal(1000, fixture.Store.ListLibrary().Count);
        Assert.Contains(fixture.Store.Search("title 42"), item => item.Title == "Title 42");
    }

    [Fact]
    public void Later_phase_records_stay_on_the_title()
    {
        using var fixture = new LibraryFixture();
        var saved = fixture.Controller.Save(Request("https://cdn.example/show/s01e01.mp4", "Pilot"));
        var profile = fixture.Store.ActiveProfileId;
        fixture.Store.SetFavorite(profile, saved.MediaId, true);
        var collection = fixture.Store.CreateCollection(profile, "Weekend");
        fixture.Store.AddToCollection(collection, saved.MediaId);
        var playlist = fixture.Store.CreatePlaylist(profile, "Queue");
        fixture.Store.AddToPlaylist(playlist, saved.MediaId);
        fixture.Store.SetIntro(saved.MediaId, 90_000);
        var pin = fixture.Protector.Protect("1234");
        fixture.Store.SetPin(profile, pin);
        fixture.Store.SaveAddon(Guid.NewGuid(), "Demo", "https://addon.example/manifest.json?token=ADDONTOKEN", "https://addon.example/manifest.json?…", true);
        fixture.Store.EnqueueSync(profile, saved.MediaId, "scrobble", "redacted");

        Assert.True(fixture.Store.ListLibrary()[0].Favorite);
        Assert.Equal(1, fixture.Store.Collections(profile)[0].Count);
        Assert.Equal(1, fixture.Store.Playlists(profile)[0].Count);
        Assert.Equal(90_000, fixture.Store.GetIntro(saved.MediaId));
        Assert.Equal("1234", fixture.Protector.Unprotect(fixture.Store.GetPin(profile)!));
        Assert.Equal(1, fixture.Store.PendingSyncCount(profile));
        var text = ReadText(fixture.DatabasePath);
        Assert.DoesNotContain("ADDONTOKEN", text);
        Assert.DoesNotContain("1234", text);
    }

    private static byte[] ReadDatabase(string path)
    {
        SqliteConnection.ClearAllPools();
        return File.ReadAllBytes(path);
    }

    private static string ReadText(string path) => System.Text.Encoding.Latin1.GetString(ReadDatabase(path));

    private static SaveRequest Request(string url, string title) =>
        new(url, title, "video", null, null, null, null, null, null, null, false);

    private static PlaybackProgress Progress(LibraryFixture fixture, Guid mediaId, long position)
    {
        var target = fixture.Store.GetPlayTarget(mediaId)!;
        return new PlaybackProgress(fixture.Store.ActiveProfileId, mediaId, target.VersionId, position, 3_600_000, false, DateTimeOffset.UtcNow, target.SourceId);
    }

    private static NewTitle Title(string title, string url)
    {
        var uri = new Uri(url);
        return new NewTitle(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "video", title, TitleNormalizer.Normalize(title),
            null, null, null, null, "DirectFile", url, UrlRedactor.Redact(uri), SourceFingerprint.Compute(uri),
            false, false, false, "", null, null);
    }
}

public sealed class LibraryFixture : IDisposable
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"defuse-{Guid.NewGuid():N}.db");
    public XorProtector Protector { get; } = new();
    public SqliteLibraryStore Store { get; }
    public LibraryController Controller { get; }

    public LibraryFixture()
    {
        Store = new SqliteLibraryStore(DatabasePath, Protector);
        Store.Initialize();
        Controller = new LibraryController(Store, Path.Combine(Path.GetTempPath(), $"defuse-art-{Guid.NewGuid():N}"));
    }

    public void Dispose()
    {
        Store.Dispose();
        SqliteConnection.ClearAllPools();
        if (File.Exists(DatabasePath))
            File.Delete(DatabasePath);
    }
}

public sealed class XorProtector : ISecretProtector
{
    public byte[] Protect(string plaintext) => System.Text.Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x5A)).ToArray();

    public string Unprotect(byte[] payload) => System.Text.Encoding.UTF8.GetString(payload.Select(b => (byte)(b ^ 0x5A)).ToArray());
}

public sealed class FakeEngine : IPlayerEngine
{
    public PlayerSnapshot Current { get; set; }
    public float Rate => 1;
    public IReadOnlyList<TrackChoice> AudioTracks => [];
    public IReadOnlyList<TrackChoice> SubtitleTracks => [];
    public IReadOnlyList<ChapterMark> Chapters => [];
    public PlaybackInfo Info => new("unknown", "unknown", 0, 0, 1);
    public Action? OnLoad { get; set; }
    public event EventHandler<PlayerSnapshot>? SnapshotChanged;
    public event EventHandler? Ended;
    public event EventHandler<PlayerFault>? Faulted;

    public void Load(Uri locator, long startPositionMs, string? subtitleLocator) => OnLoad?.Invoke();
    public void Pause() { }
    public void ResumePlayback() { }
    public void Seek(long positionMs) { }
    public void Cover(int hostWidth, int hostHeight) { }
    public void Fit() { }
    public void Stop() { }
    public void SetVolume(int volume0To100) { }
    public void SetRate(float rate) { }
    public void SelectAudio(int id) { }
    public void SelectSubtitle(int id) { }
    public void SetSubtitleDelay(long delayMs) { }
    public void SetChapter(int index) { }
    public void Dispose() { }

    public void RaiseSnapshot() => SnapshotChanged?.Invoke(this, Current);
    public void RaiseEnded() => Ended?.Invoke(this, EventArgs.Empty);
    public void RaiseFault() => Faulted?.Invoke(this, new PlayerFault(false, "Playback failed."));
}
