using Defuse.Domain;
using Defuse.Sources.Direct;
using Defuse.Sources.Library;

namespace Defuse.Application;

public sealed record SaveRequest(
    string RawUrl,
    string Title,
    string Type,
    int? Year,
    string? ShowTitle,
    int? SeasonNumber,
    int? EpisodeNumber,
    string? SubtitleUrl,
    string? PosterSourcePath,
    Guid? AttachToMediaId,
    bool PlayUnknownAsVideo);

public sealed record ImportPreview(
    ClassifiedLink Classification,
    string SuggestedTitle,
    Guid? ExistingMediaId,
    string? ExistingTitle,
    IReadOnlyList<TitleMatch> TitleMatches);

public sealed record SaveResult(Guid MediaId, bool UpdatedExisting);

public sealed class LibraryController
{
    private readonly ILibraryStore _store;
    private readonly string _artworkDirectory;

    public LibraryController(ILibraryStore store, string artworkDirectory)
    {
        _store = store;
        _artworkDirectory = artworkDirectory;
    }

    public ILibraryStore Store => _store;

    public ImportPreview Preview(string raw)
    {
        var link = LinkClassifier.Classify(raw);
        var suggested = SuggestTitle(link);
        Guid? existingId = null;
        string? existingTitle = null;
        if (Fingerprint(link) is string fingerprint && _store.FindMediaIdByFingerprint(fingerprint) is Guid id)
        {
            existingId = id;
            existingTitle = _store.GetPlayTarget(id)?.Title;
        }

        var matches = _store.FindTitleMatches(TitleNormalizer.Normalize(suggested));
        return new ImportPreview(link, suggested, existingId, existingTitle, matches);
    }

    public SaveRequest FromUrl(string rawUrl)
    {
        var link = LinkClassifier.Classify(rawUrl);
        var path = link.LocalPath ?? (link.Uri is null ? rawUrl : Uri.UnescapeDataString(link.Uri.AbsolutePath));
        var parsed = FilenameParser.Parse(path);
        var title = string.IsNullOrWhiteSpace(parsed.Title) ? SuggestTitle(link) : parsed.Title;
        var type = parsed.Season is null ? "video" : "episode";
        return new SaveRequest(rawUrl, title, type, parsed.Year, parsed.ShowTitle, parsed.Season, parsed.Episode, null, null, null, true);
    }

    public SaveResult Save(SaveRequest request)
    {
        var link = LinkClassifier.Classify(request.RawUrl);
        var type = ResolveType(link, request);
        Validate(request, type);
        if (link.Class == LinkClass.LocalFile && (link.LocalPath is null || !File.Exists(link.LocalPath)))
            throw new InvalidOperationException("That file was not found.");
        var locator = RequireLocator(link);
        var fingerprint = Fingerprint(link) ?? throw new InvalidOperationException(link.UserMessage);
        var subtitle = Subtitle(request.SubtitleUrl);
        if (_store.FindMediaIdByFingerprint(fingerprint) is Guid existingId)
        {
            var current = _store.GetPlayTarget(existingId)
                ?? throw new InvalidOperationException("That title is no longer in the library.");
            _store.UpdateLocator(current.SourceId, Build(request, link, type, locator, fingerprint, subtitle, current.MediaId, current.VersionId, current.SourceId));
            MaybePoster(existingId, request.PosterSourcePath);
            return new SaveResult(existingId, true);
        }

        if (request.AttachToMediaId is Guid attachId)
        {
            var current = _store.GetPlayTarget(attachId)
                ?? throw new InvalidOperationException("That title is no longer in the library.");
            _store.AddPreferredSource(attachId, Build(request, link, type, locator, fingerprint, subtitle, current.MediaId, current.VersionId, Guid.NewGuid()));
            MaybePoster(attachId, request.PosterSourcePath);
            return new SaveResult(attachId, true);
        }

        var mediaId = Guid.NewGuid();
        _store.CreateTitle(Build(request, link, type, locator, fingerprint, subtitle, mediaId, Guid.NewGuid(), Guid.NewGuid()));
        MaybePoster(mediaId, request.PosterSourcePath);
        return new SaveResult(mediaId, false);
    }

    public int ImportFile(string file)
    {
        if (!File.Exists(file))
            return 0;
        var parsed = FilenameParser.Parse(file);
        var result = Save(new SaveRequest(file, parsed.Title, parsed.Type, parsed.Year, parsed.ShowTitle, parsed.Season, parsed.Episode, null, null, null, false));
        return result.UpdatedExisting ? 0 : 1;
    }

    public int ImportFolder(string folder)
    {
        var count = 0;
        foreach (var file in FolderScanner.VideoFiles(folder))
            count += ImportFile(file);
        return count;
    }

    public PlayTarget Replace(Guid mediaId, string rawUrl)
    {
        var current = _store.GetPlayTarget(mediaId)
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        var before = _store.GetProgress(_store.ActiveProfileId, mediaId);
        var link = LinkClassifier.Classify(rawUrl);
        if (link.Class is LinkClass.AddonManifest or LinkClass.StremioDeepLink or LinkClass.Magnet
            or LinkClass.LocalFolder or LinkClass.UncPath or LinkClass.PlaylistFile
            or LinkClass.Unsupported or LinkClass.ExternalPage)
            throw new InvalidOperationException(link.UserMessage);
        var locator = RequireLocator(link);
        var fingerprint = Fingerprint(link) ?? throw new InvalidOperationException(link.UserMessage);
        (string? Plain, string? Redacted) subtitle = current.SubtitleLocator is null
            ? (null, null)
            : Uri.TryCreate(current.SubtitleLocator, UriKind.Absolute, out var subtitleUri)
                ? (current.SubtitleLocator, subtitleUri.IsFile ? Path.GetFileName(subtitleUri.LocalPath) : UrlRedactor.Redact(subtitleUri))
                : (current.SubtitleLocator, Path.GetFileName(current.SubtitleLocator));
        _store.UpdateLocator(current.SourceId, new NewTitle(
            current.MediaId, current.VersionId, current.SourceId, "video", current.Title,
            TitleNormalizer.Normalize(current.Title), null, current.ShowTitle, current.SeasonNumber, current.EpisodeNumber,
            link.Class.ToString(), locator, link.RedactedDisplay, fingerprint, link.MayExpire, link.IsLoopback,
            link.SuggestsStremioServer, link.UserMessage, subtitle.Plain, subtitle.Redacted));
        var after = _store.GetProgress(_store.ActiveProfileId, mediaId);
        if (before?.PositionMs != after?.PositionMs)
            throw new InvalidOperationException("Progress changed while replacing a link.");
        return _store.GetPlayTarget(mediaId)!;
    }

    public PlayTarget CreateTransient(string rawUrl, string? subtitleUrl, bool playUnknown)
    {
        var link = LinkClassifier.Classify(rawUrl);
        var playable = link.IsPlayableNow || (link.Class == LinkClass.UnknownHttp && playUnknown);
        if (!playable)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(link.UserMessage) ? "This link can't be played." : link.UserMessage);
        if (link.Class == LinkClass.LocalFile && (link.LocalPath is null || !File.Exists(link.LocalPath)))
            throw new InvalidOperationException("That file was not found.");
        var subtitle = Subtitle(subtitleUrl);
        return new PlayTarget(
            Guid.Empty, Guid.Empty, Guid.Empty, SuggestTitle(link), RequireLocator(link), subtitle.Plain,
            null, link.IsLoopback, link.MayExpire, link.SuggestsStremioServer, link.RedactedDisplay, link.UserMessage,
            null, null, null, null);
    }

    public PlayTarget? Open(Guid mediaId) => _store.GetPlayTarget(mediaId);
    public static string DisplayTitle(string title) => FilenameParser.DisplayTitle(title);

    public void RefreshParsedTitles()
    {
        foreach (var item in _store.ListLibrary())
        {
            if (item.EpisodeNumber is null || string.IsNullOrWhiteSpace(item.ShowTitle))
                continue;
            var parsed = FilenameParser.Parse(item.RedactedLocator);
            var fromFile = string.Equals(parsed.ShowTitle, item.ShowTitle, StringComparison.OrdinalIgnoreCase)
                && parsed.Episode == item.EpisodeNumber
                ? parsed.Title
                : null;
            var desired = fromFile ?? FilenameParser.DisplayTitle(item.Title);
            if (string.Equals(item.Title, desired, StringComparison.Ordinal))
                continue;
            var code = $"S{item.SeasonNumber:00}E{item.EpisodeNumber:00}";
            var generated = item.Title.Contains(code, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Title, item.ShowTitle, StringComparison.OrdinalIgnoreCase);
            if (generated)
                _store.RenameTitle(item.MediaId, desired);
        }

        foreach (var item in _store.ListLibrary())
        {
            var desired = FilenameParser.DisplayTitle(item.Title);
            if (desired.Length > 0 && !string.Equals(item.Title, desired, StringComparison.Ordinal))
                _store.RenameTitle(item.MediaId, desired);
        }
    }

    public IReadOnlyList<ContinueItem> Continue() => _store.ListContinueWatching(_store.ActiveProfileId);
    public IReadOnlyList<LibraryItem> Library() => _store.ListLibrary();
    public IReadOnlyList<LibraryItem> Search(string query) => _store.Search(query);
    public void MarkUnwatched(Guid mediaId) => _store.ClearProgress(_store.ActiveProfileId, mediaId);
    public void MarkWatched(Guid mediaId) => _store.MarkWatched(_store.ActiveProfileId, mediaId);
    public bool ClipboardWatch() => _store.GetClipboardWatch();
    public void SetClipboardWatch(bool enabled) => _store.SetClipboardWatch(enabled);
    public void SetFavorite(Guid mediaId, bool favorite) => _store.SetFavorite(_store.ActiveProfileId, mediaId, favorite);

    public void Delete(Guid mediaId)
    {
        foreach (var path in _store.DeleteTitle(mediaId))
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    public static string SuggestTitle(ClassifiedLink link)
    {
        if (link.LocalPath is not null)
            return FilenameParser.Parse(link.LocalPath).Title;
        var raw = link.Uri is null ? "" : Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(link.Uri.AbsolutePath));
        raw = raw.Replace('.', ' ').Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(raw) ? "Untitled video" : raw;
    }

    private static void Validate(SaveRequest request, string type)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
            throw new InvalidOperationException("Enter a title.");
        if (type == "episode" && (request.SeasonNumber is null || request.EpisodeNumber is null || string.IsNullOrWhiteSpace(request.ShowTitle)))
            throw new InvalidOperationException("Enter the show, season, and episode.");
    }

    private static string ResolveType(ClassifiedLink link, SaveRequest request)
    {
        var requested = request.Type switch
        {
            "video" or "movie" or "episode" or "external" => request.Type,
            _ => throw new InvalidOperationException("Choose a video, movie, episode, or external shortcut.")
        };
        return link.Class switch
        {
            LinkClass.ExternalPage => "external",
            LinkClass.UnknownHttp when !request.PlayUnknownAsVideo => "external",
            LinkClass.AddonManifest or LinkClass.StremioDeepLink or LinkClass.Magnet
                or LinkClass.LocalFolder or LinkClass.UncPath or LinkClass.PlaylistFile
                or LinkClass.Unsupported => throw new InvalidOperationException(link.UserMessage),
            LinkClass.DirectFile or LinkClass.Hls or LinkClass.Dash or LinkClass.LoopbackDirect
                or LinkClass.LocalFile or LinkClass.UnknownHttp => requested,
            _ => throw new ArgumentOutOfRangeException(nameof(link), link.Class, null)
        };
    }

    private static string RequireLocator(ClassifiedLink link) =>
        link.Uri?.AbsoluteUri ?? link.LocalPath ?? throw new InvalidOperationException(link.UserMessage);

    private static string? Fingerprint(ClassifiedLink link)
    {
        if (link.LocalPath is not null)
            return SourceFingerprint.ComputeLocal(link.LocalPath);
        return link.Uri is null ? null : SourceFingerprint.Compute(link.Uri);
    }

    private static (string? Plain, string? Redacted) Subtitle(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, null);
        var text = raw.Trim();
        if (Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile))
        {
            var redacted = uri.Scheme == Uri.UriSchemeFile ? Path.GetFileName(uri.LocalPath) : UrlRedactor.Redact(uri);
            return (text, redacted);
        }

        if (File.Exists(text))
            return (text, Path.GetFileName(text));
        throw new InvalidOperationException("Enter a subtitle URL or leave it blank.");
    }

    private static NewTitle Build(
        SaveRequest request, ClassifiedLink link, string type, string locator, string fingerprint,
        (string? Plain, string? Redacted) subtitle, Guid mediaId, Guid versionId, Guid sourceId) =>
        new(mediaId, versionId, sourceId, type, request.Title.Trim(), TitleNormalizer.Normalize(request.Title),
            request.Year, request.ShowTitle, request.SeasonNumber, request.EpisodeNumber, link.Class.ToString(),
            locator, string.IsNullOrEmpty(link.RedactedDisplay) ? Path.GetFileName(locator) : link.RedactedDisplay,
            fingerprint, link.MayExpire, link.IsLoopback, link.SuggestsStremioServer,
            link.UserMessage, subtitle.Plain, subtitle.Redacted);

    private void MaybePoster(Guid mediaId, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return;
        var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
            throw new InvalidOperationException("Poster must be a PNG, JPG, or WebP image.");
        if (!File.Exists(sourcePath))
            throw new InvalidOperationException("That poster file was not found.");
        Directory.CreateDirectory(_artworkDirectory);
        var dest = Path.Combine(_artworkDirectory, mediaId.ToString("N") + ext);
        File.Copy(sourcePath, dest, overwrite: true);
        _store.SetPoster(mediaId, dest);
    }
}
