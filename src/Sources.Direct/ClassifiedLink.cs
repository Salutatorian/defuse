namespace Defuse.Sources.Direct;

public sealed record ClassifiedLink(
    LinkClass Class,
    Uri? Uri,
    string? LocalPath,
    bool IsPlayableNow,
    bool MayExpire,
    bool IsLoopback,
    bool SuggestsStremioServer,
    string UserMessage,
    string RedactedDisplay);
