using Defuse.Domain;

namespace Defuse.Application;

public interface ISecretProtector
{
    byte[] Protect(string plaintext);
    string Unprotect(byte[] payload);
}

public sealed record NewTitle(
    Guid MediaId,
    Guid VersionId,
    Guid SourceId,
    string Type,
    string Title,
    string NormalizedTitle,
    int? Year,
    string? ShowTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    string Kind,
    string LocatorPlaintext,
    string LocatorRedacted,
    string Fingerprint,
    bool MayExpire,
    bool IsLoopback,
    bool SuggestsStremioServer,
    string? UserMessage,
    string? SubtitlePlaintext,
    string? SubtitleRedacted);

public sealed record TitleMatch(Guid MediaId, string Title);

public sealed record PlayTarget(
    Guid MediaId,
    Guid VersionId,
    Guid SourceId,
    string Title,
    string Locator,
    string? SubtitleLocator,
    bool? SeekableKnown,
    bool IsLoopback,
    bool MayExpire,
    bool SuggestsStremioServer,
    string RedactedLocator,
    string? DependencyNote,
    string? ShowTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    PlaybackProgress? Progress);

public sealed record ContinueItem(
    Guid MediaId,
    string Title,
    long PositionMs,
    long DurationMs,
    string RedactedLocator,
    string? PosterPath);

public sealed record LibraryItem(
    Guid MediaId,
    string Title,
    string Type,
    string RedactedLocator,
    string? PosterPath,
    long? PositionMs,
    bool Completed,
    string? ShowTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    bool Favorite,
    long? DurationMs = null);

public sealed record SourceChoice(Guid SourceId, string Kind, string RedactedLocator, bool Preferred);

public sealed record ProfileCard(Guid Id, string Name, bool HasPin);

public sealed record CollectionCard(Guid Id, string Name, int Count);

public sealed record PlaylistCard(Guid Id, string Name, int Count);

public sealed record AddonCard(Guid Id, string Name, string RedactedUrl, bool Enabled);

public sealed record ConnectionCard(Guid Id, string Protocol, string Label, string RedactedEndpoint);

public interface ILibraryStore
{
    void Initialize();
    Guid ActiveProfileId { get; }
    Guid? FindMediaIdByFingerprint(string fingerprint);
    IReadOnlyList<TitleMatch> FindTitleMatches(string normalizedTitle);
    void CreateTitle(NewTitle title);
    void UpdateLocator(Guid sourceId, NewTitle replacement);
    void AddPreferredSource(Guid mediaId, NewTitle source);
    PlayTarget? GetPlayTarget(Guid mediaId);
    PlayTarget? OpenSource(Guid mediaId, Guid sourceId);
    IReadOnlyList<SourceChoice> ListSources(Guid mediaId);
    void UpsertProgress(PlaybackProgress progress);
    PlaybackProgress? GetProgress(Guid profileId, Guid mediaId);
    void ClearProgress(Guid profileId, Guid mediaId);
    void MarkWatched(Guid profileId, Guid mediaId);
    IReadOnlyList<ContinueItem> ListContinueWatching(Guid profileId);
    IReadOnlyList<LibraryItem> ListLibrary();
    IReadOnlyList<LibraryItem> Search(string query);
    IReadOnlyList<LibraryItem> Episodes(string showTitle);
    IReadOnlyList<string> Shows();
    void SetSeekable(Guid sourceId, bool seekable);
    void SetPoster(Guid mediaId, string cachePath);
    void RenameTitle(Guid mediaId, string title);
    IReadOnlyList<string> DeleteTitle(Guid mediaId);
    bool GetClipboardWatch();
    void SetClipboardWatch(bool enabled);
    string? GetSetting(string key);
    void SetSetting(string key, string? value);
    void SetFavorite(Guid profileId, Guid mediaId, bool favorite);
    IReadOnlyList<CollectionCard> Collections(Guid profileId);
    Guid CreateCollection(Guid profileId, string name);
    void AddToCollection(Guid collectionId, Guid mediaId);
    IReadOnlyList<PlaylistCard> Playlists(Guid profileId);
    Guid CreatePlaylist(Guid profileId, string name);
    void AddToPlaylist(Guid playlistId, Guid mediaId);
    IReadOnlyList<ProfileCard> Profiles();
    Guid CreateProfile(string name);
    void SwitchProfile(Guid profileId);
    void SetPin(Guid profileId, byte[]? protectedPin);
    byte[]? GetPin(Guid profileId);
    void SaveAddon(Guid id, string name, string manifestPlain, string redactedUrl, bool enabled);
    IReadOnlyList<AddonCard> Addons();
    string? AddonManifest(Guid id);
    void SaveConnection(Guid id, string protocol, string label, string endpointPlain, string redacted, string? secretPlain);
    IReadOnlyList<ConnectionCard> Connections();
    (string Endpoint, string? Secret) OpenConnection(Guid id);
    void DeleteConnection(Guid id);
    void SetIntro(Guid mediaId, long endMs);
    long? GetIntro(Guid mediaId);
    void EnqueueSync(Guid profileId, Guid? mediaId, string eventType, string redactedPayload);
    int PendingSyncCount(Guid profileId);
}
