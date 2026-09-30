# Feature parity

The inventory and stage for every row lives in [master-plan.md](master-plan.md) section 2. This file tracks only what the current build claims. Update a row when a demo passes, and point at the evidence in [sample-matrix.md](sample-matrix.md).

Stages P1–P3 are not started. Do not mark a row done because a dependency was installed.

## P0 — first useful release

| Capability | Status | Plan |
| --- | --- | --- |
| Paste one direct video URL, name it, save it in the library | Not started | First-release plan, tasks 6–7 |
| Play that URL inside the app | Not started | Tasks 2 and 7 |
| Clipboard offer only after the user opts in | Not started | Task 7 |
| Drag and drop a URL onto the window | Not started | Task 7 |
| Optional manual poster | Not started | Tasks 6–7 |
| Optional subtitle URL passed to the player | Not started | Tasks 2 and 6 |
| Play without saving | Not started | Tasks 6–7 |
| Pause, seek ±10 seconds, fullscreen, volume, keyboard | Not started | Tasks 2 and 7 |
| Player fills the window; controls fade until mouse or key input | Not started | Task 7 |
| Failed playback stays on screen with retry, another source, and replace | Not started | Task 7 |
| Resume after about 30 seconds and before 95% | Not started | Tasks 4 and 7 |
| Continue Watching | Not started | Tasks 5–7 |
| Progress flush at start, every 5 seconds, pause, seek, and close | Not started | Tasks 4 and 7 |
| Replace link keeps title and position | Not started | Tasks 5–7 |
| Mark unwatched and remove a title | Not started | Tasks 5–7 |
| Redacted diagnostics; locator protected with DPAPI | Not started | Tasks 3 and 5 |
| Refusal messages for addon manifests, `stremio://`, magnets, folders, UNC, playlists | Not started | Task 3 |
| Single local video file | Not started | Tasks 3 and 6 |
| Open-with and custom URL scheme | Out of this release | Master plan phase 6 / P2 deep links |

## Explicitly not claimed

HDR, Dolby Vision, Atmos, passthrough, torrents, cloud sign-in, and live TV are not implemented. Compact mode is an always-on-top window, not operating-system picture-in-picture.

Folder scan, network-folder connect, Stremio stream fetch, Jellyfin, Emby, Plex library listing, WebDAV, FTP links, SFTP listing, TMDB poster lookup, a local Trakt queue, and profiles are in the app. They have not been demonstrated against a real server or file in sample-matrix.md. SFTP listing does not start playback, so the password is not handed to the player. Plex lists libraries and does not play them. Cloud providers are named and left disconnected.

Automated tests cover resume rules, link classification, and the SQLite library: a token is not stored in clear text, replacing a link keeps the saved position, and continue-watching uses the 30-second to 95% window. The desktop process starts. A sample was not played in this pass.
