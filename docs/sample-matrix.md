# Sample matrix

Record results on this PC. A public URL that disappears gets a replacement URL written here; do not treat a dead sample host as an engine failure until a second sample fails the same way.

Machine: Windows 10/11, x64, .NET SDK 10.0.302. GPU and display: fill in during the playback spike.

| Sample | URL or file | Play | Seek | Overlay | Fullscreen | Notes |
| --- | --- | --- | --- | --- | --- | --- |
| Local MP4 | Downloaded public MP4 | | | | | |
| HTTP MP4 | `https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4` | | | | | |
| HTTP MKV | `https://filesamples.com/samples/video/mkv/sample_640x360.mkv` | | | | | |
| HLS VOD | `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8` | | | | | |
| User's copied link | Redacted form only | | | | | Do not paste the raw token into this file |
| Expired or 401 link | | | | | | Title and position must remain |
| Loopback while server is closed | | | | | | Expect the local-server message |
| Addon `manifest.json` | | | | | | Must not open the decoder |
| `stremio://` | | | | | | Must not open the decoder |

| State check | Result | Notes |
| --- | --- | --- |
| Quit at a known timestamp and resume | | Target: near 42:10 when that is the quit point |
| Kill the process between 5-second writes | | Loss is at most one interval |
| Replace URL | | Same media id and position |
| Token absent from `library.db` and logs | | Search the file for a known token |
| Home lists 1,000 stored titles | | Automated test plus a scroll check |
| Dead URL leaves the UI responsive | | |
