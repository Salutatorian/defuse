namespace Defuse.Sources.Direct;

public static class PlaybackFailureCopy
{
    public static string Describe(bool isLoopback, bool suggestsStremio, bool mayExpire, bool authenticationFailed)
    {
        if (suggestsStremio)
            return "If this link came from Stremio, open Stremio on this PC, then retry.";
        if (isLoopback)
            return "Open the program on this PC that serves this link, then retry.";
        if (authenticationFailed || mayExpire)
            return "Update this video's link. The title and your place are still saved.";
        return "Playback failed. Retry, or replace the link. The title is still in your library.";
    }
}
