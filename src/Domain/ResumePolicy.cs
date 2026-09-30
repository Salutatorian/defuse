namespace Defuse.Domain;

public static class ResumePolicy
{
    public const long ResumeAfterMs = 30_000;
    public const double CompletionRatio = 0.95;

    public static bool IsComplete(long positionMs, long durationMs)
    {
        if (durationMs <= 0 || positionMs < 0)
            return false;
        return positionMs >= (long)(durationMs * CompletionRatio);
    }

    public static ResumeDecision Evaluate(PlaybackProgress? saved, PlaybackCapabilities caps)
    {
        if (caps.IsLive)
            return ResumeDecision.Unseekable("Live video doesn't have an exact place to resume.");
        if (!caps.Seekable)
            return ResumeDecision.Unseekable("This video can't resume at an exact time.");
        if (saved is null || (saved.PositionMs <= 0 && !saved.Completed))
            return ResumeDecision.FromStart();
        if (saved.Completed || IsComplete(saved.PositionMs, saved.DurationMs))
            return ResumeDecision.Completed();
        if (saved.DurationMs <= 0)
            return ResumeDecision.Unseekable("This video can't resume at an exact time.");
        if (saved.PositionMs < ResumeAfterMs)
            return ResumeDecision.ResumeQuiet(saved.PositionMs);
        return ResumeDecision.OfferResume(saved.PositionMs);
    }
}
