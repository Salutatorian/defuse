# Open source media player: master plan

Captured 2026-09-26 from the product contract. This file is the product contract. It is not an implementation checklist.

Working scope: Windows first, with architecture that can support macOS, Linux, and TV/mobile clients later.

Reference snapshot: Infuse 8.5-era product and Firecore guides reviewed September 26, 2026.

Primary user journey: paste a playable video link, save it as a title, close the app, come back later, and resume at the exact position in a polished library.

The first build is specified in [docs/superpowers/plans/2026-09-26-first-useful-release.md](superpowers/plans/2026-09-26-first-useful-release.md). Phases 3–10 stay in this document until that journey works.

Parity status for the first release is tracked in [feature-parity.md](feature-parity.md). Hardware results go in [sample-matrix.md](sample-matrix.md).

## 1. Product contract

Build an original, open source desktop media player inspired by Infuse's ease of use and library quality. Use an original name, graphics, layouts, and branding. The video player is inside the app. A user can add one link without creating a text file, setting up a server, or installing a Stremio addon. Saved links appear alongside local and network videos in the same library.

The first completed build must do five things reliably: paste, save, play, resume, and recover. The wider feature set follows in phases. “All Infuse features” is the parity backlog below, not a promise that proprietary platform capabilities, licensed codecs, and every cloud provider can ship in the first release.

### What counts as a link

| Input | Interpretation | Initial behavior |
| --- | --- | --- |
| `https://.../movie.mkv`, MP4, WebM, TS, etc. | Direct playable video URL | Save and play inside the app. |
| HTTP(S) HLS `.m3u8` or MPEG-DASH `.mpd` | Stream manifest | Play; determine if seekable and finite. |
| `http://127.0.0.1:...` | Stream served by software on this computer | Play on the same PC while that server is running; display this dependency. |
| URL with expiring token or signed query string | Temporary playable URL | Save title and progress independently of URL; prompt for a refreshed URL when expired. |
| Local file, local folder, SMB path | Personal library media | Import, index, and play. |
| M3U playlist or `.strm` file | List of streams or pointer to one stream | Import in a later phase, keeping the original mapping editable. |
| `https://.../manifest.json` | Stremio addon descriptor | Add as a provider only after addon integration ships; never feed it to the video decoder. |
| `stremio://...` | Stremio deep link | Parse as an app/provider action if supported later; never pretend it is a video URL. |
| Magnet/torrent | Torrent source, not an HTTP video | Outside the first release. A later adapter may use a properly configured local streaming service. |
| Netflix or other web page link | Page or DRM service link | Save as an external shortcut if desired; do not claim in-app playback. |

Privacy rule: links can contain access tokens and private addresses. Show redacted versions in diagnostics, encrypt saved credentials, avoid copying tokens into TMDB or telemetry requests, and keep the original URL local unless the user explicitly enables sync.

## 2. Infuse feature inventory and parity decisions

Status key: P0 first useful build; P1 polished personal player; P2 broad library and source coverage; P3 deep parity or platform-specific work. “Adapt” means implement the outcome with a Windows-appropriate interaction. “Evaluate” means a technical or licensing spike is required before making a capability claim.

### A. Adding media and source access

| Infuse behavior or feature | Our implementation | Stage |
| --- | --- | --- |
| Direct URL links, saved bookmarks, link sharing from other apps | Paste URL modal, clipboard detection with consent, drag/drop URL, open-with and custom deep link later; save to the main library | P0 |
| Play multiple incoming URLs, optional filenames, subtitles, starting positions | Batch import and optional per-item overrides, with an import preview | P1 |
| `.strm` files containing HTTP streams | File import and watched-folder scanner, title inferred from filename | P1 |
| `.strmlnk` external title links | External-link card with “Open in browser/app” | P2 |
| Local files and folders | File picker, watched folders, rescan, file rename handling | P1 |
| SMB shares and Windows network paths | Share browser and saved credentials; UNC path support | P1 |
| NFS, FTP, FTPS, FTPES, SFTP, WebDAV, HTTP/HTTPS directory browsing | Separate source adapters with connection tests and per-protocol coverage | P2 |
| UPnP/DLNA discovery and playback | Device discovery and remote browsing, with stated indexing limits | P3 |
| Plex, Jellyfin, Emby | Dedicated APIs for libraries, versions, artwork, playlists, watched state, and transcode choices | P2 |
| Dropbox, Google Drive, OneDrive, Box, pCloud, MEGA, Yandex, and other cloud drives | Provider plugins using each provider's documented authentication and range requests | P3 |
| Remote access away from home | Connect to a user-provided secure server/VPN address; connection diagnostics | P2 |
| Offline copies and downloads | Download only when the source permits it, with disk quota, progress, resume, cleanup | P2 |
| Browser and local transfer workflows | Drag/drop and local folders first; optional local LAN upload endpoint later | P3 |

Infuse officially lists additional cloud providers such as Aliyun Drive, 123 Drive, 115 Drive, Baidu NetDisk, and GuangYaPan. Treat each as a separate integration with its own availability, authentication, and maintenance review. Do not imply they all work through one generic cloud adapter.

### B. The library and discovery experience

| Infuse behavior or feature | Our implementation | Stage |
| --- | --- | --- |
| Posters, backdrops, logos, description, cast, genre, rating, runtime, trailer | TMDB matching, cached metadata, title details view; manual corrections | P1 |
| Movie, series, season, episode organization | Media hierarchy with stable IDs and episode-specific progress | P1 |
| Filename matching, year and external IDs, anime and special episode naming | Parser with configurable rules and explicit TMDB/IMDb IDs | P1 |
| Manual match and local/embedded metadata, NFO/XML | Match picker, “use file metadata” mode, local overrides with precedence rules | P2 |
| Local/embedded artwork and custom posters, fanart, logos, season art | Artwork priority: manual override, local/embedded, TMDB | P2 |
| Search and filters by title, person, genre, status, quality, resolution, rating | Fast local database search with filters and sort | P1 |
| Home rows, Up Next, Continue Watching, Recently Added, Picks of the Day | Customizable Home with clear criteria for each row | P1 |
| Favorites, pinned folders, lists, customizable Home | Pin/unpin, reorder, hide, rename | P1 |
| Watched indicators, ratings, mark watched/unwatched | Per-profile state, manual overrides | P1 |
| Spoiler hiding for unwatched episodes | Hide synopsis and thumbnails until played | P2 |
| Automatic movie collections and custom collections | TMDB collection links and arbitrary user collections | P2 |
| Smart groups for duplicates, cuts, parts, and versions | Group physical sources under a title; preserve separate versions | P2 |
| Smart folders for a movie folder and its assets | Folder flattening with an on/off setting | P2 |
| Playlists, continuous playback, shuffle, loop | Saved playlists separate from collections; playback queue | P2 |
| Extras, trailers, featurettes, deleted scenes | Attach and show extras on detail pages | P2 |
| Refresh/index controls, artwork/details pre-cache | Incremental scanner, manual refresh, cache budget and status | P2 |
| User profiles and family-specific libraries | Per-profile progress, favorites, home, settings, access | P2 |
| Parental controls, rating restrictions, PIN and app lock | Separate profile rules and protected settings | P3 |

Deliberate improvement: pasted URLs become normal library items immediately. The app must not silently assume a URL contains a trustworthy movie title. Offer a manual title and poster while metadata matching runs.

### C. Playback, video, and audio

| Infuse behavior or feature | Our implementation | Stage |
| --- | --- | --- |
| Native playback of common containers and codecs without server transcoding | Embedded native engine with hardware decode where supported | P0/P1 |
| Pause, seek, skip, full-screen, volume, keyboard shortcuts | In-app controls with a responsive overlay | P0 |
| Resume playback, restart, per-item state | Persist seconds and duration; offer resume or start over | P0 |
| Continue to next episode, next-file selection | Queue and episode ordering with an option to disable | P1 |
| Chapter navigation | Read container chapters and expose a chapter menu | P1 |
| Playback speed, aspect ratio, zoom/crop, audio delay, subtitle delay | Per-item overrides plus global defaults | P1 |
| Choose audio/video/subtitle track and preferred languages | Saved preference with a fallback for unavailable tracks | P1 |
| On-screen stream info, codec and bitrate details | Optional technical panel | P1 |
| Intelligent buffering, read-ahead choices, speed test, stall recovery | Configurable cache and source retry; explain network failures | P2 |
| Picture in picture and compact player | Always-on-top mini-player with native controls | P2 |
| Scrub previews | Generated thumbnails or remote provider previews when supported | P2 |
| Intro, recap, and credit skip; manual or automatic | Chapter markers or opted-in community timestamps; user correction | P2 |
| HDR10, HDR10+, HLG, Dolby Vision variants | Per-format hardware and renderer test matrix; fallbacks if unsupported | Evaluate/P2/P3 |
| Dolby Digital, DTS, TrueHD, DTS-HD, multichannel output | Verify decode, downmix, passthrough, and licensing independently on Windows hardware | Evaluate/P2/P3 |
| AI video upscaling | Optional GPU capability later; never block basic playback | P3 |
| DVD/BDMV, ISO, VIDEO_TS, VVC, and uncommon formats | Add only after engine-specific tests and platform constraints are known | P3 |
| Cast/AirPlay and wireless playback | Separate output adapters; version-specific limitations disclosed | P3 |
| Server-supplied alternate versions/transcodes | Expose when a Plex/Emby/Jellyfin adapter supports them | P2 |

The official format list is a research inventory, not a blanket support claim. Container support, decode support, HDR output, and audio passthrough are separate tests. Dolby Vision profiles and Atmos output depend on hardware, display, OS, codec handling, and sometimes licensing.

### D. Subtitles, personalization, sync, and automation

| Infuse behavior or feature | Our implementation | Stage |
| --- | --- | --- |
| Embedded subtitles and sidecar `.srt`, `.ass`, `.vtt`, PGS, etc. | Enumerate tracks; load local/URL sidecars; test rendered formats separately | P1/P2 |
| On-demand OpenSubtitles search | Optional authenticated integration by title, episode, language, and file hash | P2 |
| Subtitle size, font, color, position, delay, and visibility | Player controls and per-profile defaults | P1/P2 |
| Per-video audio/subtitle/zoom settings | Preferences bound to a stable media item/version ID | P1 |
| Dark/light, grid/list, poster titles, sorting, language preferences | User settings and adaptive desktop layouts | P1 |
| iCloud syncing of saved links, lists, metadata choices, watched state, progress | Our own optional sync service or user-hosted sync; local-only default | P3 |
| Trakt history, progress, ratings, and comments | Optional OAuth integration, conflict resolution, and a retry queue | P2 |
| Plex/Emby/Jellyfin two-way watched state | Per-adapter sync with server identity mapping | P2 |
| Deep links to a title, play with timestamp, save URLs from another app | App URL scheme and a documented local import API | P2 |
| Backup/restore, import/export | Versioned JSON export, SQLite backup, portable media references | P1 |
| Apple-only gestures, Home Screen/Top Shelf, Live Activities, Face ID, Vision Pro environment | Future platform work only if a client is built for that OS | P3 |

## 3. Platform and architecture decision

### Initial target

Ship an open source Windows 10/11 desktop application first. Later platforms get dedicated adapters and their own acceptance tests. A Windows executable does not become an Apple TV or Vision Pro app by recompiling it.

### Recommended first implementation

| Layer | Choice | Reason / boundary |
| --- | --- | --- |
| Desktop UI | .NET desktop UI with WPF and original visual components | Mature Windows support and control over a desktop library. The UI is an original design. |
| Playback | LibVLCSharp.WPF with the official Windows LibVLC package, behind `IPlayerEngine` | Official VideoLAN integration supports hardware playback and an in-app video view. Prototype overlay, full screen, seek, and HDR behavior before committing. |
| Metadata and application state | SQLite with migrations and repository interfaces | Local-first, fast startup, durable progress, easy export. |
| Images and metadata | TMDB client behind `IMetadataProvider` | Poster, backdrop, cast, episode, and collection metadata. Manual edits must survive refresh. |
| OS credentials | Windows Credential Manager / DPAPI adapter | Keep cloud and network passwords away from SQLite rows and logs. |
| Background work | Bounded queue for metadata/artwork fetch and folder scan | Home appears immediately. A slow remote source does not freeze the UI. |
| Packaging | Signed Windows installer once distributable | Clean updates and uninstall. Development run via `dotnet run` first. |

Why this stack: VideoLAN documents WPF hardware-rendered playback and the need to handle video overlays carefully. A plain web video tag in a React/Electron/Tauri shell will not deliver reliable playback of the broad format set, and embedding native mpv under a webview brings window/overlay complexity. A Tauri or Avalonia UI can be reconsidered after a playback spike proves video compositing, full screen, subtitle overlay, and GPU behavior. Do not lock the entire UI stack before that spike.

Keep the domain model and source adapters separate from WPF so a future platform can reuse the rules and data format. The playback engine is swappable, with a spike for libmpv if LibVLCSharp cannot meet the quality bar for the user's sample streams.

### Components

```text
App UI -> Library service -> SQLite
       -> Playback coordinator -> IPlayerEngine (LibVLC first)
       -> Source resolver -> Direct URL | Local | SMB | Stremio addon | Server/cloud adapters
       -> Metadata service -> TMDB | Local/NFO | Server metadata
       -> Sync service -> optional Trakt | optional user sync
```

Only the playback coordinator can start or stop playback. Source adapters return a playable descriptor, not UI elements. Metadata enriches a saved media item and never overwrites its title, progress, or custom poster without consent.

## 4. Data model and critical invariants

| Entity | Minimum fields |
| --- | --- |
| Profile | ID, name, avatar, parental rule, settings |
| MediaItem | Stable UUID, type (movie/show/episode/video/live/external), title, normalized title, year, parent IDs, TMDB/IMDb IDs, custom fields |
| MediaVersion | Stable UUID, media ID, cut/edition, quality, codec hints, runtime, preferred-source flag |
| Source | Stable UUID, version ID, kind, opaque locator, provider ID, auth reference, expiry estimate, last validation, capability flags |
| PlaybackState | Profile ID, media/version ID, position ms, duration ms, updated time, completion flag, last source ID |
| Artwork | Media ID, role, provider, URI/cache path, manual-override flag |
| Subtitle | Media/version ID, language, origin, locator, format, offset |
| Collection, Playlist, Favorite | Profile ownership, order and membership, optional custom art |
| SourceConnection | Protocol, endpoint, display label, credential reference, scan settings |
| ProviderAddon | Manifest URL, declared capabilities, enabled catalogs, configuration, last fetch |
| SyncEvent | Profile/media ID, event type, time, revision, upstream result |

Invariants:

1. Media identity must survive a source URL change. A fresh link replaces a Source, not the MediaItem or its progress.
2. Progress is keyed by profile and stable media/version ID, never by a full signed URL. Persist on playback start, every 5 seconds during playback, pause, seek, source switch, window close, and graceful shutdown. Journal periodic writes so a crash loses at most the latest short interval.
3. The resume prompt appears after about 30 seconds and before near-completion. The completion threshold is configurable, initially 95%. Explicit Mark Unwatched and Restart override automatic progress.
4. Live streams have no finite resume timestamp. Videos with no seek support may save watched state and must not claim exact seeking.
5. Source failure cannot erase the item. Show Replace Link, Try another source, or Reconnect provider, preserving the saved position.
6. Tokens are never used as stable identifiers, exposed in logs, or sent to analytics. Source URLs and credentials stay out of screenshots and bug reports unless the user chooses to reveal them.
7. Remote metadata is cached with provider attribution. Manual changes take precedence and survive rescans.

## 5. Link import and Stremio-specific design

### P0: direct stream input

1. The user clicks Add Link or pastes into the app.
2. The parser classifies direct URL, local loopback URL, HLS/DASH, manifest, deep link, or unsupported page. Do not equate all HTTPS links with playable streams.
3. Show a preview with an editable title, movie/show/episode, optional season and episode, poster, optional subtitle URL, and a Save to Library button. Allow Play Without Saving.
4. Validate with a short, cancellable probe and the player itself. A HEAD failure alone must not reject a URL. Follow redirects carefully. Detect range/seek support where possible.
5. Deduplicate by a user-confirmed title/external ID or a safe normalized source fingerprint, never by the token-bearing full URL alone.
6. Save media and source transactionally, start the player, then persist progress.

### P1: sources can change while a title stays put

Show all saved sources on title details. Let the user rename, disable, reorder, replace, remove, test, and attach a new source without losing the existing watch state. Treat a loopback URL as tied to the program on this computer that serves it. If that program is closed or its service restarts, show an actionable error. Signed remote URLs may expire independently of the app.

### P2: optional Stremio-compatible provider integration

The Stremio addon protocol provides `/manifest.json` and catalog, metadata, stream, and subtitle resources. Build a provider manager that imports a user's HTTPS manifest, displays its declared capabilities, and asks for title/episode streams when playback starts. Store a stable addon media ID and episode ID. Fetch a fresh stream URL at play time. This solves many expired-link cases where a configured provider can still supply a current source. Do not promise that arbitrary addons work, that private addons expose usable URLs, or that a direct video player can play a torrent or DRM object. Test provider types individually. A local streaming server is an optional, separately labeled dependency for non-HTTP sources.

For sports and IPTV work, treat live channels as a later provider category: catalog/channel IDs, EPG now/next, stream URL refresh, mini-player, and a channel list beside video. Live playback and VOD resume have different state models.

### Failure messages

| Failure | User-facing action |
| --- | --- |
| URL is an addon manifest | “This is an addon link. Add it under Providers” after addon support ships. Until then, say provider setup is not available yet. |
| `stremio://` deep link | Explain the supported import route. Do not send it to the player. |
| Local streaming server offline | “Open the program on this PC that serves this link, then retry.” Mention Stremio when the link looks like its local server. |
| 401/403 or expired signature | “Update this video's link” while retaining title and progress. |
| Network timeout | Retry, diagnostics, alternate source. Leave the library unchanged. |
| Unsupported codec/container | Show codec info and alternative source/player details. Do not mark watched. |
| Nonseekable or live | Disable precise resume and explain why. |

## 6. UI and interaction spec

### Screens

The first version designs Home, Library, Details, Show page, and Player, plus Add Link and Search as overlays. The Source picker is part of that version: it is the way to replace a dead link without losing the place. Downloads, Profiles, and Live TV get their own screens when those features are built. They are not empty pages in the first build.

| Area | Purpose | Format | When |
| --- | --- | --- | --- |
| Home | Continue Watching and the rest of the library | Full page | First version |
| Library | Movies, shows, other videos, and links as filters of one library | Full page | First version |
| Details | One title: artwork, resume or play, and source status | Full page | First version |
| Show page | Seasons, episodes, watched ticks, and Play Next | Full page | First version layout. Episode rows fill in when a show is saved. Play Next waits for episode ordering. |
| Player | The movie or episode while it is playing | Full page | First version |
| Add Link | Paste, name, and save one link | Overlay | First version |
| Search | Find a saved movie, show, episode, or playlist | Overlay from anywhere | First version searches saved titles. People and genres arrive with metadata. |
| Source picker | Choose another link or quality for the same title | Panel over Details or Player | First version |
| Collections and Playlists | Organize saved titles | Section within Library | When organization ships |
| Connections | Folders, network shares, and Stremio addons | Section within Settings | When those sources ship |
| Downloads | Offline copies and storage | Full page | Later |
| Profiles | Separate watch progress for different people | Switcher | Later |
| Live TV | Channels, guide, and a small player beside the list | Full page | Later |

### Player

When playback starts, the video fills the window and the controls fade out. Moving the mouse or pressing a key brings them back. Reduced motion keeps the controls visible instead of fading them. The player is its own view, not a bar left on top of the library.

| Area | Controls | First version |
| --- | --- | --- |
| Top left | Back to Details, movie title, and episode name when the item has one | Working |
| Center | Brief play/pause feedback. Skip Intro or Next Episode when the title has that marker or a next episode | Play/pause feedback only. Skip Intro and Next Episode appear when those features exist. |
| Bottom | Play/pause, current time, seek bar, remaining time, volume, fullscreen | Working. The seek bar shows buffered progress when the engine reports it. Thumbnail previews come later. |
| Bottom right | Audio, subtitles, playback speed, picture in picture, source picker, and more settings | Source picker works. The other buttons open the more panel or stay inactive until that control works. |

The source picker switches to another saved link or quality and continues at the same timestamp. Replacing a link does the same. If the link fails, the video surface stays up with Retry, Choose Another Source, and Replace Link. The watch position stays saved.

Less-used controls live in one panel: subtitle language, subtitle size and delay, audio track, audio delay, aspect ratio, chapters, playback information, and Start Over. The first version includes Start Over and playback information that the engine can already report. Track selection, delay, aspect, and chapters light up as playback polish lands. Picture in picture is not part of the first player.

Progress is saved while the video plays, again on pause, and again when leaving the player, so Continue Watching is ready on return.

### Information architecture

- Home: Continue Watching, Recently Added, Favorites, Movies, Shows, and user-pinned rows. Live is a later page, not a first-version row.
- Library: Movies, TV Shows, Other Videos, Links, and later Collections and Playlists. Links is a filter of the same library, not a separate storage silo.
- Search: an overlay. Instant results over saved titles, keyboard focus, and later people and genres.
- Details: poster, description when metadata exists, resume or play, source status, and the source picker.
- Show page: seasons, episodes, watched marks, and Play Next once episode order exists.
- Player: the layout in the table above.
- Settings: playback, subtitles and audio, appearance, privacy, cache, about, and shortcuts. Connections is the section for folders, shares, and addons.

### Paste-link journey acceptance

```text
Home -> Add Link -> link preview -> Save & Play
     -> Player fills the window -> close at 42:10
     -> reopen -> Continue Watching -> Details -> Resume
     -> Player starts near 42:10
     -> link expired -> video stays up -> Replace Link or another source
     -> Resume still near 42:10
```

Keyboard and mouse first. Minimize clicks for repeat use. A poster row should be smoothly virtualized and populated from cached data so opening the app is fast even with slow networks. Provide loading skeletons, source status, and a usable offline library shell. Make empty states and failures specific.

### Original visual direction

Dark cinematic surface, readable type, restrained accent color, large poster imagery, compact metadata, and clear focus states. Use keyboard-accessible overlays and a reduced-motion preference. Infuse is a usability reference, not a design asset pack: original iconography, spacing, composition, name, and assets.

## 7. Delivery phases and gates

These are capability phases, not calendar promises. Each phase ends with a usable build and a short demo. Do not advance through a broken paste/resume flow just to cover more checkboxes.

| Phase | Output | Done when |
| --- | --- | --- |
| 0. Playback spike | Standalone Windows proof with LibVLCSharp WPF, HTTP MP4, MKV, HLS, hardware decode, seek, subtitle overlay, fullscreen | Four sample sources play. Overlay is stable. Memory and GPU behavior are acceptable. Change engine or host only if measured failures justify it. |
| 1. App foundation | Solution/repo, SQLite migrations, app shell, logging, packaging, CI, profile skeleton | Cold start shows Home fast. A schema migration preserves prior data. |
| 2. First useful release | Paste URL, preview/edit, save, player, progress, Continue Watching, replace URL | The paste/close/reopen/resume/replace workflow passes on a real copied HTTP URL and a stable public sample. |
| 3. Personal library | Local files/folders, SMB, scanning, TMDB, posters, shows/seasons/episodes, search | A folder with multiple shows indexes correctly. Wrong metadata can be fixed without losing progress. |
| 4. Playback polish | Audio/subtitle track picker, sidecars, language preferences, chapters, speeds, keyboard controls, error recovery | Multiple codecs and subtitle types behave consistently. The player survives sleep/wake and network interruption. |
| 5. Organization | Favorites, lists, collections, playlists, duplicate versions, watched overrides, cache controls | An identical title with two links appears once and keeps both playable versions. |
| 6. Stremio provider | Manifest import, catalog/metadata/stream/subtitle requests, fresh URL resolution | A provider-backed title reopens after its previous direct URL expires without losing progress, when the provider has a valid replacement. |
| 7. Server/network | Jellyfin, then Emby/Plex, WebDAV/NFS/SFTP/FTP, remote streaming | Each adapter has contract tests and independent auth, refresh, metadata, and playback behavior. |
| 8. Advanced playback | PiP, live previews, intro skipping, versions/transcode selection, HDR/audio test matrix | Each format claim has a recorded hardware/OS test and a documented fallback. |
| 9. Sync and profiles | Trakt, optional user sync, multi-profile state, backup/restore, parental controls | Conflict tests and offline/reconnect cases pass without progress regression. |
| 10. Cloud/mobile/TV | Individual cloud connectors and any new platform clients | Feature parity is tracked per platform. Users never infer support from a Windows-only success. |

### Initial test matrix

- Source kinds: local MP4 and MKV; public HTTP MP4 with Range; HTTP source without Range; HLS VOD; live HLS; copied link with a loopback host; signed URL expiring; invalid/unreachable URL.
- Player: seek near start/end; fast repeated seeking; subtitle track switch; audio track switch; pause/quit/resume; fullscreen/window resize; hardware decode toggle; laptop sleep/wake.
- State: app crash between progress writes; duplicate title; replace URL; season/episode identity; poster override; metadata mismatch; deleted local file; disconnected SMB; offline launch.
- Privacy: token not shown in logs; database backup export options; credential deletion and share disconnect.
- Performance: quick Home shell with 1,000 cached items; poster virtualization; concurrent scan bounded; no input freeze while probing a dead URL.

## 8. Technical spikes, constraints, and dependencies

| Decision or risk | How to resolve before claiming support |
| --- | --- |
| WPF video overlay and exclusive fullscreen | Official LibVLCSharp WPF view uses a native child window and an overlay workaround. Prototype the source picker and on-video controls on the user's GPU. |
| LibVLC versus libmpv | Measure the same four user-relevant samples for startup, seeking, HDR, subtitle switching, CPU/GPU, and installer size. Keep the engine interface swappable. |
| HDR and Dolby behaviors | Test on an actual display/receiver. Format support in marketing copy is not proof of output mode. Consider licensing and distribution before release. |
| Stremio links | Test a redacted example from the user's setup for direct HTTP, loopback, debrid, addon manifest, and deep-link cases. Never store a temporary stream as the title's sole identity. |
| TMDB | Obtain developer API access, cache politely, include the required TMDB attribution and logo. Revisit terms if commercializing. |
| OpenSubtitles and Trakt | Use their documented APIs and credential flows. Cache results and respect quota and terms. |
| Cross-device sync | Define offline-first merge semantics and encryption before deploying a service. A personal single-device app should not require a server. |
| Cloud/network credentials | Use OS secure storage. Avoid writing secrets to exports by default. |
| Distribution and licenses | Audit LibVLC/codec, SDK, artwork, subtitle, and icon licenses for the chosen open source project license before publishing. |

## 9. Suggested repo shape and executable first backlog

```text
src/
  Desktop/             WPF app, views, theme, shortcuts
  Domain/              Media IDs, profile, progress, contracts
  Application/         Import, playback coordinator, library use cases
  Persistence/         SQLite migrations and repositories
  Playback.LibVLC/     Native playback adapter
  Sources.Direct/      HTTP, HLS/DASH, local and loopback
  Sources.Stremio/     Manifest protocol adapter (phase 6)
  Metadata.TMDB/       Search, match, cache, attribution
  Integrations/        Later server, subtitle, sync adapters
tests/
  Domain.Tests/        Progress/identity/expiry behavior
  Adapter.Tests/       Stream classification and provider fixtures
  Desktop.Smoke/       User-critical playback workflows
docs/
  feature-parity.md    Each row from section 2 with status and evidence
  sample-matrix.md     Tested samples and Windows hardware results
```

First build backlog, in order:

1. Create a native video-view spike with sample HTTP MP4, MKV, and HLS. Confirm overlay and seeking.
2. Create the original visual shell: Home, Add Link, Details, Player.
3. Add SQLite migrations for MediaItem, Source, Profile, and PlaybackState.
4. Implement the URL classifier, import preview, and Play Without Saving.
5. Implement Save & Play and Continue Watching.
6. Implement periodic progress, close/crash recovery, and Resume/Start Over.
7. Implement Replace Link, preserving identity, metadata, and playback position.
8. Run an installable dev build, test with a real URL from the user's setup, and log any unsupported link class as a provider task.

The first release omits cloud accounts, account sync, EPG, torrents, and AI upscaling. They remain in the parity backlog with the prerequisites above. The opening workflow is useful without them.

## 10. Success criteria and open decisions

The personal MVP is successful when a user can paste a copied playable URL into the app, name it, see it on Home with a poster or a manual image, watch, quit, reopen, and resume, then replace an expired URL without losing the title or progress. Starting playback must require no text editor, `.strm` file, server registration, or external player.

Decisions still open, and they do not block the playback spike or the initial domain model:

- Original public project name. The repository folder is `defuse`; the first plan uses Defuse as a working code name.
- Whether Windows-first remains correct. The contract says yes.
- One redacted sample of the user's copied link format.
- Whether a later phase should include live sports in the UI. The first plan does not.
- Intended open source license. Do not add a `LICENSE` file until this is chosen.

### Source notes

This plan audits publicly documented features. Some Infuse functions differ by platform or by free/Pro tier. Refer to the current official docs when implementing a phase.

- Infuse feature and technical specifications
- Infuse settings overview
- Direct URL save/play API
- STRM files and STRMLNK files
- Library scanning and indexing
- Favorites and lists, collections, playlists
- Network share protocols, cloud providers, Plex/Emby/Jellyfin features
- Subtitles, playback controls, audio
- iCloud sync feature list, Trakt integration
- Infuse 8.4 additions and Infuse 8.5 additions
- Stremio addon protocol
- Official LibVLCSharp WPF integration notes
- TMDB API terms and attribution
