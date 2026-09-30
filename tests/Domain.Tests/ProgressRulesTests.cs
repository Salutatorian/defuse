using Defuse.Domain;

namespace Defuse.Domain.Tests;

public class ProgressRulesTests
{
    private static readonly Guid Profile = Guid.Parse("8f4e2c10-6b3a-4d77-9c1e-2a5b7d9e0f11");
    private static readonly Guid Media = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Version = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Source = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public void Resume_under_thirty_seconds_is_quiet()
    {
        var saved = Row(29_000, 600_000, false);
        var decision = ResumePolicy.Evaluate(saved, new PlaybackCapabilities(true, false));
        Assert.Equal(ResumeKind.ResumeQuiet, decision.Kind);
        Assert.Equal(29_000, decision.PositionMs);
    }

    [Fact]
    public void Resume_after_thirty_seconds_is_offered()
    {
        var decision = ResumePolicy.Evaluate(Row(30_000, 600_000, false), new PlaybackCapabilities(true, false));
        Assert.Equal(ResumeKind.OfferResume, decision.Kind);
    }

    [Fact]
    public void Finished_and_near_the_end_start_over()
    {
        Assert.Equal(ResumeKind.Completed, ResumePolicy.Evaluate(Row(1, 0, true), new PlaybackCapabilities(true, false)).Kind);
        Assert.Equal(ResumeKind.Completed, ResumePolicy.Evaluate(Row(570_000, 600_000, false), new PlaybackCapabilities(true, false)).Kind);
    }

    [Fact]
    public void Live_and_unseekable_do_not_claim_a_timestamp()
    {
        var saved = Row(120_000, 600_000, false);
        Assert.Equal(ResumeKind.Unseekable, ResumePolicy.Evaluate(saved, new PlaybackCapabilities(true, true)).Kind);
        Assert.Equal(ResumeKind.Unseekable, ResumePolicy.Evaluate(saved, new PlaybackCapabilities(false, false)).Kind);
        Assert.Equal(ResumeKind.Unseekable, ResumePolicy.Evaluate(Row(120_000, 0, false), new PlaybackCapabilities(true, false)).Kind);
    }

    [Fact]
    public void Tracker_flushes_at_start_and_at_five_seconds()
    {
        var tracker = new ProgressTracker();
        var start = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var first = tracker.Start(Profile, Media, Version, Source, 1_000, 600_000, start);
        Assert.Equal(1_000, first.PositionMs);

        Assert.Null(tracker.Observe(4_000, 600_000, start.AddSeconds(4.9)));
        var flushed = tracker.Observe(6_000, 600_000, start.AddSeconds(5));
        Assert.NotNull(flushed);
        Assert.Equal(6_000, flushed.PositionMs);
    }

    [Fact]
    public void Fingerprint_ignores_query_tokens_and_keeps_id()
    {
        var withToken = SourceFingerprint.Compute(new Uri("https://cdn.example/video.mp4?id=abc&token=one"));
        var rotated = SourceFingerprint.Compute(new Uri("https://cdn.example/video.mp4?token=two&id=abc"));
        var other = SourceFingerprint.Compute(new Uri("https://cdn.example/video.mp4?id=xyz&token=one"));
        Assert.Equal(withToken, rotated);
        Assert.NotEqual(withToken, other);
    }

    [Fact]
    public void Redactor_drops_userinfo_and_query()
    {
        var redacted = UrlRedactor.Redact(new Uri("https://user:secret@cdn.example/video.mp4?token=abc"));
        Assert.DoesNotContain("secret", redacted);
        Assert.DoesNotContain("token", redacted);
        Assert.EndsWith("?…", redacted);
    }

    [Fact]
    public void Redactor_drops_path_keys_and_keeps_the_filename()
    {
        var key = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ABCD";
        var hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var redacted = UrlRedactor.Redact(new Uri($"https://cdn.example/resolve/{key}/{hash}/0/Show.S01E01.mkv"));
        Assert.DoesNotContain(key, redacted);
        Assert.DoesNotContain(hash, redacted);
        Assert.Contains("Show.S01E01.mkv", redacted);
        Assert.Contains("cdn.example", redacted);
    }

    [Fact]
    public void Scrubber_strips_urls_and_secret_pairs()
    {
        var scrubbed = LogScrubber.Scrub("failed https://cdn.example/a.mp4?token=abc token=visible");
        Assert.DoesNotContain("abc", scrubbed);
        Assert.DoesNotContain("visible", scrubbed);
    }

    private static PlaybackProgress Row(long position, long duration, bool completed) =>
        new(Profile, Media, Version, position, duration, completed, DateTimeOffset.UnixEpoch, Source);
}
