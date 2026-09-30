using Defuse.Domain;

namespace Defuse.Application;

public sealed class PlaybackCoordinator
{
    private readonly IPlayerEngine _engine;
    private readonly ILibraryStore _store;
    private readonly ProgressTracker _tracker = new();
    private PlayTarget? _current;
    private bool _persist;
    private bool _seekableNoted;

    public PlaybackCoordinator(IPlayerEngine engine, ILibraryStore store)
    {
        _engine = engine;
        _store = store;
    }

    public PlayTarget? Current => _current;
    public float Rate => _engine.Rate;
    public IReadOnlyList<TrackChoice> AudioTracks => _engine.AudioTracks;
    public IReadOnlyList<TrackChoice> SubtitleTracks => _engine.SubtitleTracks;
    public IReadOnlyList<ChapterMark> Chapters => _engine.Chapters;
    public PlaybackInfo Info => _engine.Info;

    public ResumeDecision Decide(PlayTarget target)
    {
        var duration = target.Progress?.DurationMs ?? 0;
        var seekable = target.SeekableKnown ?? duration > 0;
        var live = target.SeekableKnown == false && duration <= 0;
        return ResumePolicy.Evaluate(target.Progress, new PlaybackCapabilities(seekable, live));
    }

    public void Play(PlayTarget target, long startMs, bool persist)
    {
        _current = target;
        _persist = persist && target.MediaId != Guid.Empty;
        _seekableNoted = target.SeekableKnown is not null;
        if (_persist)
        {
            var row = _tracker.Start(
                _store.ActiveProfileId, target.MediaId, target.VersionId, target.SourceId, startMs,
                target.Progress?.DurationMs ?? 0, DateTimeOffset.UtcNow);
            _store.UpsertProgress(row);
        }

        _engine.Load(ToUri(target.Locator), startMs, target.SubtitleLocator);
    }

    public void OnSnapshot(PlayerSnapshot snapshot)
    {
        Note(snapshot.PositionMs, snapshot.DurationMs, force: false);
        NoteSeekable(snapshot);
    }

    public void Pause()
    {
        _engine.Pause();
        Flush();
    }

    public void ResumePlayback() => _engine.ResumePlayback();

    public void Seek(long positionMs, long durationMs)
    {
        _engine.Seek(positionMs);
        Note(positionMs, durationMs, force: true);
    }

    public void Cover(int hostWidth, int hostHeight) => _engine.Cover(hostWidth, hostHeight);
    public void Fit() => _engine.Fit();

    public void SetRate(float rate) => _engine.SetRate(rate);
    public void SelectAudio(int id) => _engine.SelectAudio(id);
    public void SelectSubtitle(int id) => _engine.SelectSubtitle(id);
    public void SetSubtitleDelay(long delayMs) => _engine.SetSubtitleDelay(delayMs);
    public void SetChapter(int index) => _engine.SetChapter(index);

    public void MarkFinished()
    {
        if (!_persist || _current is null)
            return;
        _tracker.KeepComplete();
        Flush();
    }

    public void Flush()
    {
        var snapshot = _engine.Current;
        Note(snapshot.PositionMs, snapshot.DurationMs, force: true);
    }

    public void StopPersisting() => _persist = false;

    public void Stop()
    {
        Flush();
        _persist = false;
        _current = null;
        _engine.Stop();
    }

    private void Note(long positionMs, long durationMs, bool force)
    {
        if (!_persist || _current is null)
            return;
        var now = DateTimeOffset.UtcNow;
        var row = force
            ? _tracker.Flush(positionMs, durationMs, now)
            : _tracker.Observe(positionMs, durationMs, now);
        if (row is not null)
            _store.UpsertProgress(row);
    }

    private void NoteSeekable(PlayerSnapshot snapshot)
    {
        if (_current is null || _seekableNoted || _current.SourceId == Guid.Empty)
            return;
        if (snapshot.DurationMs > 0)
        {
            _store.SetSeekable(_current.SourceId, snapshot.IsSeekable);
            _seekableNoted = true;
        }
        else if (snapshot.PlaybackStarted && !snapshot.IsSeekable)
        {
            _store.SetSeekable(_current.SourceId, false);
            _seekableNoted = true;
        }
    }

    private static Uri ToUri(string locator)
    {
        if (Uri.TryCreate(locator, UriKind.Absolute, out var uri) && uri.IsAbsoluteUri && !uri.IsFile)
            return uri;
        if (File.Exists(locator))
            return new Uri(Path.GetFullPath(locator));
        if (Uri.TryCreate(locator, UriKind.Absolute, out uri))
            return uri;
        throw new InvalidOperationException("That link cannot be opened.");
    }
}
