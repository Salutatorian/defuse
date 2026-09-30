namespace Defuse.Application;

public readonly record struct PlayerSnapshot(
    long PositionMs,
    long DurationMs,
    bool IsSeekable,
    bool IsPaused,
    bool PlaybackStarted,
    long? BufferedMs);

public sealed record PlayerFault(bool AuthenticationFailed, string SafeMessage);

public sealed record TrackChoice(int Id, string Name);

public sealed record ChapterMark(int Index, string Name, long OffsetMs);

public sealed record PlaybackInfo(string Video, string Audio, int Width, int Height, float Rate);

public interface IUiMarshal
{
    void Post(Action action);
}

public interface IPlayerEngine : IDisposable
{
    PlayerSnapshot Current { get; }
    float Rate { get; }
    IReadOnlyList<TrackChoice> AudioTracks { get; }
    IReadOnlyList<TrackChoice> SubtitleTracks { get; }
    IReadOnlyList<ChapterMark> Chapters { get; }
    PlaybackInfo Info { get; }
    event EventHandler<PlayerSnapshot>? SnapshotChanged;
    event EventHandler? Ended;
    event EventHandler<PlayerFault>? Faulted;

    void Load(Uri locator, long startPositionMs, string? subtitleLocator);
    void Pause();
    void ResumePlayback();
    void Seek(long positionMs);
    void Cover(int hostWidth, int hostHeight);
    void Fit();
    void Stop();
    void SetVolume(int volume0To100);
    void SetRate(float rate);
    void SelectAudio(int id);
    void SelectSubtitle(int id);
    void SetSubtitleDelay(long delayMs);
    void SetChapter(int index);
}
