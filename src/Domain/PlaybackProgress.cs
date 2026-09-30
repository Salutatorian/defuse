namespace Defuse.Domain;

public sealed record PlaybackProgress(
    Guid ProfileId,
    Guid MediaId,
    Guid VersionId,
    long PositionMs,
    long DurationMs,
    bool Completed,
    DateTimeOffset UpdatedAt,
    Guid SourceId);

public readonly record struct PlaybackCapabilities(bool Seekable, bool IsLive);

public enum ResumeKind
{
    FromStart,
    ResumeQuiet,
    OfferResume,
    Completed,
    Unseekable
}

public readonly record struct ResumeDecision(ResumeKind Kind, long PositionMs, string Explanation)
{
    public static ResumeDecision FromStart() => new(ResumeKind.FromStart, 0, "Play from the start.");
    public static ResumeDecision ResumeQuiet(long positionMs) => new(ResumeKind.ResumeQuiet, positionMs, "Resume.");
    public static ResumeDecision OfferResume(long positionMs) => new(ResumeKind.OfferResume, positionMs, "Resume from where you left off.");
    public static ResumeDecision Completed() => new(ResumeKind.Completed, 0, "Start over. This was already finished.");
    public static ResumeDecision Unseekable(string explanation) => new(ResumeKind.Unseekable, 0, explanation);
}
