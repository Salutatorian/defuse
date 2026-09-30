using Defuse.Application;
using Defuse.Domain;

namespace Defuse.Desktop;

static class RemoteTitles
{
    public static Guid SaveLocator(ILibraryStore store, string title, string locator, string kind, bool mayExpire, string? subtitleLocator = null)
    {
        var uri = new Uri(locator);
        var fingerprint = SourceFingerprint.Compute(uri);
        if (store.FindMediaIdByFingerprint(fingerprint) is Guid existing)
            return existing;
        var mediaId = Guid.NewGuid();
        store.CreateTitle(new NewTitle(
            mediaId, Guid.NewGuid(), Guid.NewGuid(), "video", title.Trim(), TitleNormalizer.Normalize(title),
            null, null, null, null, kind, locator, UrlRedactor.Redact(uri), fingerprint,
            mayExpire, false, false, null, subtitleLocator, subtitleLocator is null ? null : "subtitle"));
        return mediaId;
    }
}
