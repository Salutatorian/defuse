namespace Defuse.Domain;

public sealed class ProgressTracker
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private PlaybackProgress? _current;
    private DateTimeOffset _lastFlush;
    private bool _keepComplete;

    public void KeepComplete() => _keepComplete = true;

    public PlaybackProgress Start(
        Guid profileId,
        Guid mediaId,
        Guid versionId,
        Guid sourceId,
        long positionMs,
        long durationMs,
        DateTimeOffset now)
    {
        _keepComplete = false;
        _current = new PlaybackProgress(
            profileId, mediaId, versionId, positionMs, durationMs,
            ResumePolicy.IsComplete(positionMs, durationMs), now, sourceId);
        _lastFlush = now;
        return _current;
    }

    public PlaybackProgress? Observe(long positionMs, long durationMs, DateTimeOffset now)
    {
        var updated = Update(positionMs, durationMs, now);
        if (now - _lastFlush < FlushInterval)
            return null;
        _lastFlush = now;
        return updated;
    }

    public PlaybackProgress Flush(long positionMs, long durationMs, DateTimeOffset now)
    {
        var updated = Update(positionMs, durationMs, now);
        _lastFlush = now;
        return updated;
    }

    private PlaybackProgress Update(long positionMs, long durationMs, DateTimeOffset now)
    {
        if (_current is null)
            throw new InvalidOperationException("Playback has not started.");
        _current = _current with
        {
            PositionMs = positionMs,
            DurationMs = durationMs,
            UpdatedAt = now,
            Completed = _keepComplete || ResumePolicy.IsComplete(positionMs, durationMs)
        };
        return _current;
    }
}
