# First useful release implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** On Windows, paste one playable video URL, name it, watch it inside the app, quit, reopen, and resume near the same position, then replace an expired URL without losing the title or that position.

**Architecture:** WPF shell on .NET 10. LibVLCSharp sits behind `IPlayerEngine`. SQLite stores the library and progress. DPAPI protects the saved URL. Domain rules do not reference WPF or LibVLC. Only `PlaybackCoordinator` calls load, pause, seek, and stop. The window may read `LibVlcEngine.Player` to attach the video surface.

**Tech Stack:** .NET 10 (`10.0.302` on this machine), WPF, LibVLCSharp.WPF 3.10.1, VideoLAN.LibVLC.Windows 3.0.24, Microsoft.Data.Sqlite 10.0.12, System.Security.Cryptography.ProtectedData 10.0.12, xUnit via `dotnet new xunit`.

**Working name:** Defuse. Original UI. The repository folder is the code name until a public name is chosen. Infuse is a usability reference, not a source of assets or layout.

**Product contract:** `docs/master-plan.md`. This plan is phases 0–2 only.

**Commits:** Each task lists a commit command. Create that commit only when the user has asked for commits. Until then, run the tests and leave the working tree as it is.

**Stop rule:** If Task 2 cannot play the four samples with a stable overlay, stop before Task 7. Tasks 3–6 are engine-independent and may still be completed. Do not build the library shell on an engine that fails the spike.

**Out of this plan:** TMDB, watched folders, SMB/UNC playback, Stremio addon protocol, torrents, cloud accounts, sync, EPG, HDR or Atmos claims, intro skip, picture in picture, open-with, and a custom URL scheme. A single local video file is in. A folder is refused with a specific message.

---

## File map

| Path | Responsibility |
| --- | --- |
| `src/Domain` | Profile id, title normalization, resume policy, progress tracker, URL redaction, fingerprint, log scrubbing |
| `src/Sources.Direct` | Classify a pasted string. Map a failure to user-facing copy |
| `src/Application` | Store and player interfaces, import/replace/continue, playback coordinator |
| `src/Persistence` | SQLite schema v1, DPAPI protector |
| `src/Playback.LibVLC` | LibVLC engine. No library policy |
| `src/Desktop` | WPF shell, theme, windows |
| `tests/Domain.Tests` | Resume and progress interval |
| `tests/Adapter.Tests` | Classification, redaction, fingerprint |
| `tests/Persistence.Tests` | Database, privacy, import, replace |

Namespaces match assembly names: `Defuse.Domain`, `Defuse.Sources.Direct`, `Defuse.Application`, `Defuse.Persistence`, `Defuse.Playback.LibVLC`, `Defuse.Desktop`.

## Task 1: Solution skeleton

**Files:**
- Create: `global.json`
- Create: `Directory.Build.props`
- Create: `.github/workflows/test.yml`
- Create: the projects below
- Modify: `.gitignore` only if a generated ignore is missing the entries already in the repo

- [ ] **Step 1: Pin the SDK and shared project settings**

Create `global.json`:

```json
{
  "sdk": {
    "version": "10.0.302",
    "rollForward": "latestMinor",
    "allowPrerelease": false
  }
}
```

Create `Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
  </PropertyGroup>
</Project>
```

- [ ] **Step 2: Create projects**

Run from the repository root:

```powershell
dotnet new sln -n Defuse
dotnet new classlib -n Defuse.Domain -o src/Domain -f net10.0
dotnet new classlib -n Defuse.Sources.Direct -o src/Sources.Direct -f net10.0
dotnet new classlib -n Defuse.Application -o src/Application -f net10.0
dotnet new classlib -n Defuse.Persistence -o src/Persistence -f net10.0
dotnet new classlib -n Defuse.Playback.LibVLC -o src/Playback.LibVLC -f net10.0
dotnet new wpf -n Defuse.Desktop -o src/Desktop -f net10.0
dotnet new xunit -n Defuse.Domain.Tests -o tests/Domain.Tests -f net10.0
dotnet new xunit -n Defuse.Adapter.Tests -o tests/Adapter.Tests -f net10.0
dotnet new xunit -n Defuse.Persistence.Tests -o tests/Persistence.Tests -f net10.0
dotnet sln Defuse.sln add (Get-ChildItem -Recurse -Filter *.csproj).FullName
Remove-Item src/Domain/Class1.cs, src/Sources.Direct/Class1.cs, src/Application/Class1.cs, src/Persistence/Class1.cs, src/Playback.LibVLC/Class1.cs, tests/Domain.Tests/UnitTest1.cs, tests/Adapter.Tests/UnitTest1.cs, tests/Persistence.Tests/UnitTest1.cs
dotnet add src/Sources.Direct/Defuse.Sources.Direct.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add src/Application/Defuse.Application.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add src/Application/Defuse.Application.csproj reference src/Sources.Direct/Defuse.Sources.Direct.csproj
dotnet add src/Persistence/Defuse.Persistence.csproj reference src/Application/Defuse.Application.csproj
dotnet add src/Persistence/Defuse.Persistence.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add src/Playback.LibVLC/Defuse.Playback.LibVLC.csproj reference src/Application/Defuse.Application.csproj
dotnet add src/Playback.LibVLC/Defuse.Playback.LibVLC.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add src/Desktop/Defuse.Desktop.csproj reference src/Application/Defuse.Application.csproj
dotnet add src/Desktop/Defuse.Desktop.csproj reference src/Persistence/Defuse.Persistence.csproj
dotnet add src/Desktop/Defuse.Desktop.csproj reference src/Playback.LibVLC/Defuse.Playback.LibVLC.csproj
dotnet add src/Desktop/Defuse.Desktop.csproj reference src/Sources.Direct/Defuse.Sources.Direct.csproj
dotnet add src/Desktop/Defuse.Desktop.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add tests/Domain.Tests/Defuse.Domain.Tests.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add tests/Adapter.Tests/Defuse.Adapter.Tests.csproj reference src/Sources.Direct/Defuse.Sources.Direct.csproj
dotnet add tests/Adapter.Tests/Defuse.Adapter.Tests.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add tests/Persistence.Tests/Defuse.Persistence.Tests.csproj reference src/Persistence/Defuse.Persistence.csproj
dotnet add tests/Persistence.Tests/Defuse.Persistence.Tests.csproj reference src/Application/Defuse.Application.csproj
dotnet add tests/Persistence.Tests/Defuse.Persistence.Tests.csproj reference src/Domain/Defuse.Domain.csproj
dotnet add tests/Persistence.Tests/Defuse.Persistence.Tests.csproj reference src/Sources.Direct/Defuse.Sources.Direct.csproj
dotnet add src/Persistence/Defuse.Persistence.csproj package Microsoft.Data.Sqlite --version 10.0.12
dotnet add src/Persistence/Defuse.Persistence.csproj package System.Security.Cryptography.ProtectedData --version 10.0.12
```

- [ ] **Step 3: Make the playback project a WPF library**

Replace `src/Playback.LibVLC/Defuse.Playback.LibVLC.csproj` with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <RootNamespace>Defuse.Playback.LibVLC</RootNamespace>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="LibVLCSharp.WPF" Version="3.10.1" />
    <PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.24" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Application\Defuse.Application.csproj" />
    <ProjectReference Include="..\Domain\Defuse.Domain.csproj" />
  </ItemGroup>
</Project>
```

Run:

```powershell
dotnet restore Defuse.sln
```

Expected: restore succeeds.

- [ ] **Step 4: Add CI for the engine-independent tests**

Create `.github/workflows/test.yml`:

```yaml
name: test
on:
  push:
  pull_request:
jobs:
  test:
    runs-on: windows-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - run: dotnet test tests/Domain.Tests/Defuse.Domain.Tests.csproj --configuration Release
      - run: dotnet test tests/Adapter.Tests/Defuse.Adapter.Tests.csproj --configuration Release
      - run: dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj --configuration Release
```

CI does not launch the WPF player. Video proof stays on this machine in Task 2 and Task 8.

- [ ] **Step 5: Commit when commits are requested**

```powershell
git add global.json Directory.Build.props Defuse.sln src tests .github/workflows/test.yml
git commit -m "build: add the Defuse solution skeleton"
```

## Task 2: Playback spike

**Files:**
- Create: `src/Application/PlayerContracts.cs`
- Create: `src/Playback.LibVLC/LibVlcEngine.cs`
- Create: `src/Desktop/SpikeWindow.xaml`
- Create: `src/Desktop/SpikeWindow.xaml.cs`
- Modify: `src/Desktop/App.xaml`
- Modify: `src/Desktop/App.xaml.cs`
- Modify: `docs/sample-matrix.md`

The spike window is temporary. Task 7 replaces startup with `MainWindow` and deletes `SpikeWindow`.

- [ ] **Step 1: Add the player contract**

Create `src/Application/PlayerContracts.cs`. Application does not reference LibVLC. The window attaches `LibVlcEngine.Player` and is the only type allowed to touch that property.

```csharp
namespace Defuse.Application;

public readonly record struct PlayerSnapshot(
    long PositionMs,
    long DurationMs,
    bool IsSeekable,
    bool IsPaused,
    bool PlaybackStarted);

public sealed record PlayerFault(bool AuthenticationFailed, string SafeMessage);

public interface IUiMarshal
{
    void Post(Action action);
}

public interface IPlayerEngine : IDisposable
{
    PlayerSnapshot Current { get; }
    event EventHandler<PlayerSnapshot>? SnapshotChanged;
    event EventHandler? Ended;
    event EventHandler<PlayerFault>? Faulted;

    void Load(Uri locator, long startPositionMs, string? subtitleLocator);
    void Pause();
    void ResumePlayback();
    void Seek(long positionMs);
    void Stop();
    void SetVolume(int volume0To100);
}
```

- [ ] **Step 2: Add the LibVLC engine**

Create `src/Playback.LibVLC/LibVlcEngine.cs`:

```csharp
using Defuse.Application;
using Defuse.Domain;
using LibVLCSharp.Shared;

namespace Defuse.Playback.LibVLC;

public sealed class LibVlcEngine : IPlayerEngine
{
    private readonly LibVLC _libVlc;
    private readonly MediaPlayer _player;
    private readonly IUiMarshal _ui;
    private Media? _media;
    private long _pendingStartMs;
    private bool _startApplied;
    private bool _authFailed;
    private long _lastPostTick;
    private bool _disposed;

    public LibVlcEngine(IUiMarshal ui)
    {
        _ui = ui;
        Core.Initialize();
        _libVlc = new LibVLC("--no-video-title-show");
        _player = new MediaPlayer(_libVlc);
        _libVlc.Log += OnLog;
        _player.TimeChanged += (_, _) => ThrottledPublish();
        _player.LengthChanged += (_, _) => Publish();
        _player.Playing += (_, _) => OnPlaying();
        _player.Paused += (_, _) => Publish();
        _player.EndReached += (_, _) => OnEndReached();
        _player.EncounteredError += (_, _) => OnError();
    }

    public MediaPlayer Player => _player;

    public PlayerSnapshot Current { get; private set; }

    public event EventHandler<PlayerSnapshot>? SnapshotChanged;
    public event EventHandler? Ended;
    public event EventHandler<PlayerFault>? Faulted;

    public void Load(Uri locator, long startPositionMs, string? subtitleLocator)
    {
        _authFailed = false;
        _startApplied = false;
        _pendingStartMs = startPositionMs;
        _media?.Dispose();
        _media = new Media(_libVlc, locator);
        if (!string.IsNullOrWhiteSpace(subtitleLocator))
        {
            try
            {
                _media.AddSlave(MediaSlaveType.Subtitle, 4, subtitleLocator);
            }
            catch (Exception ex)
            {
                RaiseFault(false, LogScrubber.Scrub(ex.Message));
                return;
            }
        }

        _player.Play(_media);
    }

    public void Pause() => _player.SetPause(true);

    public void ResumePlayback() => _player.SetPause(false);

    public void Seek(long positionMs) => _player.Time = positionMs;

    public void Stop() => _player.Stop();

    public void SetVolume(int volume0To100) => _player.Volume = Math.Clamp(volume0To100, 0, 100);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _player.Stop();
        _player.Dispose();
        _media?.Dispose();
        _libVlc.Dispose();
    }

    private void OnLog(object? sender, LogEventArgs e)
    {
        var message = e.Message ?? "";
        if (message.Contains("401", StringComparison.Ordinal)
            || message.Contains("403", StringComparison.Ordinal)
            || message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase)
            || message.Contains("Unauthorized", StringComparison.OrdinalIgnoreCase))
        {
            _authFailed = true;
        }
    }

    private void OnPlaying()
    {
        if (!_startApplied && _pendingStartMs > 0)
        {
            _startApplied = true;
            _player.Time = _pendingStartMs;
        }

        Publish();
    }

    private void OnEndReached()
    {
        _ui.Post(() =>
        {
            Current = Current with { PositionMs = Math.Max(Current.PositionMs, Current.DurationMs) };
            Ended?.Invoke(this, EventArgs.Empty);
        });
    }

    private void OnError()
    {
        var auth = _authFailed;
        _ui.Post(() => RaiseFault(auth, "Playback failed."));
    }

    private void RaiseFault(bool authenticationFailed, string safeMessage)
    {
        Faulted?.Invoke(this, new PlayerFault(authenticationFailed, safeMessage));
    }

    private void ThrottledPublish()
    {
        var now = Environment.TickCount64;
        if (now - _lastPostTick < 250)
            return;
        _lastPostTick = now;
        Publish();
    }

    private void Publish()
    {
        var position = _player.Time;
        var duration = _player.Length;
        var seekable = _player.IsSeekable;
        var paused = !_player.IsPlaying;
        var started = duration > 0 || _player.IsPlaying;
        _ui.Post(() =>
        {
            Current = new PlayerSnapshot(position, duration, seekable, paused, started);
            SnapshotChanged?.Invoke(this, Current);
        });
    }
}
```

`OnLog` must not write `e.Message` to a file, the console, or the UI. The URL is in that string.

- [ ] **Step 3: Add the spike window**

Create `src/Desktop/WpfUiMarshal.cs`:

```csharp
using System.Windows.Threading;
using Defuse.Application;

namespace Defuse.Desktop;

public sealed class WpfUiMarshal : IUiMarshal
{
    private readonly Dispatcher _dispatcher;

    public WpfUiMarshal(Dispatcher dispatcher) => _dispatcher = dispatcher;

    public void Post(Action action) => _dispatcher.BeginInvoke(action);
}
```

Create `src/Desktop/SpikeWindow.xaml`:

```xml
<Window x:Class="Defuse.Desktop.SpikeWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Defuse spike"
        Width="1100"
        Height="700"
        Background="#121418">
    <DockPanel>
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Margin="12">
            <TextBox x:Name="UrlBox" Width="640" />
            <Button Content="Play" Margin="8,0,0,0" Click="Play_Click" />
            <Button Content="Pause" Margin="8,0,0,0" Click="Pause_Click" />
            <Button Content="+10s" Margin="8,0,0,0" Click="Forward_Click" />
            <Button Content="Fullscreen" Margin="8,0,0,0" Click="Fullscreen_Click" />
        </StackPanel>
        <TextBlock x:Name="Status" DockPanel.Dock="Bottom" Margin="12" Foreground="#F4F1EA" />
        <ContentControl x:Name="VideoHost" />
    </DockPanel>
</Window>
```

Create `src/Desktop/SpikeWindow.xaml.cs`:

```csharp
using System.Windows;
using System.Windows.Controls;
using Defuse.Playback.LibVLC;
using LibVLCSharp.WPF;

namespace Defuse.Desktop;

public partial class SpikeWindow : Window
{
    private readonly LibVlcEngine _engine;
    private VideoView? _video;
    private bool _fullscreen;

    public SpikeWindow(LibVlcEngine engine)
    {
        _engine = engine;
        InitializeComponent();
        _engine.SnapshotChanged += (_, snapshot) =>
            Status.Text = $"{snapshot.PositionMs / 1000}s / {snapshot.DurationMs / 1000}s";
        _engine.Faulted += (_, fault) => Status.Text = fault.SafeMessage;
        KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Escape && _fullscreen)
                ToggleFullscreen();
        };
    }

    private void Play_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(UrlBox.Text.Trim(), UriKind.Absolute, out var uri))
        {
            Status.Text = "Enter an absolute URL or file URI.";
            return;
        }

        _video ??= new VideoView();
        VideoHost.Content = _video;
        _video.MediaPlayer = _engine.Player;
        _engine.Load(uri, 0, null);
    }

    private void Pause_Click(object sender, RoutedEventArgs e) => _engine.Pause();

    private void Forward_Click(object sender, RoutedEventArgs e) =>
        _engine.Seek(_engine.Current.PositionMs + 10_000);

    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private void ToggleFullscreen()
    {
        _fullscreen = !_fullscreen;
        WindowStyle = _fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
        WindowState = _fullscreen ? WindowState.Maximized : WindowState.Normal;
    }
}
```

Replace `src/Desktop/App.xaml` with:

```xml
<Application x:Class="Defuse.Desktop.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
</Application>
```

Replace `src/Desktop/App.xaml.cs` with:

```csharp
using System.Windows;
using Defuse.Desktop;
using Defuse.Playback.LibVLC;

namespace Defuse.Desktop;

public partial class App : Application
{
    private LibVlcEngine? _engine;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _engine = new LibVlcEngine(new WpfUiMarshal(Dispatcher));
        var window = new SpikeWindow(_engine);
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _engine?.Dispose();
        base.OnExit(e);
    }
}
```

If the template's `App` namespace conflicts, keep one `App` class and delete the duplicate. The WPF `App` class lives in `App.xaml.cs` only.

- [ ] **Step 4: Build and play four samples**

```powershell
dotnet build src/Desktop/Defuse.Desktop.csproj
dotnet run --project src/Desktop/Defuse.Desktop.csproj
```

Expected: the spike window opens.

Download a local file, then play these four, one at a time. Record pass or fail in `docs/sample-matrix.md`. Write what you saw. Do not claim HDR, Atmos, or exclusive fullscreen.

```powershell
Invoke-WebRequest -Uri "https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4" -OutFile "$env:TEMP\defuse-sample.mp4"
```

| Sample | What to pass to Play |
| --- | --- |
| Local MP4 | `file:///` URI of `$env:TEMP\defuse-sample.mp4` |
| HTTP MP4 | `https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4` |
| HTTP MKV | `https://filesamples.com/samples/video/mkv/sample_640x360.mkv` |
| HLS VOD | `https://test-streams.mux.dev/x36xhzz/x36xhzz.m3u8` |

For each sample, confirm:

- Picture is visible inside the window.
- Pause stops the clock.
- `+10s` moves the position forward when the stream is seekable.
- Fullscreen covers the screen, and Escape returns to a normal window.
- After Stop is not in this window yet: closing the app does not leave a stuck video window on the desktop.

If a host returns 404, substitute another public sample of the same container, write the URL in `docs/sample-matrix.md`, and retry once. Two different hosts failing the same container is an engine finding, not a bad link.

Overlay check: put a `TextBlock` inside a `VideoView` by changing the host to a `VideoView` whose child is a `TextBlock` with the word `Overlay`. Confirm the word is visible above the picture. The LibVLCSharp WPF view renders that child in a separate window. Do not use a WPF `Popup` over the video.

- [ ] **Step 5: Commit when commits are requested**

```powershell
git add src/Application/PlayerContracts.cs src/Playback.LibVLC src/Desktop docs/sample-matrix.md
git commit -m "feat: prove in-app playback with LibVLCSharp"
```

## Task 3: Classification, redaction, and fingerprint

**Files:**
- Create: `src/Domain/SecretQuery.cs`
- Create: `src/Domain/UrlRedactor.cs`
- Create: `src/Domain/SourceFingerprint.cs`
- Create: `src/Domain/LogScrubber.cs`
- Create: `src/Sources.Direct/LinkClass.cs`
- Create: `src/Sources.Direct/ClassifiedLink.cs`
- Create: `src/Sources.Direct/LinkClassifier.cs`
- Create: `src/Sources.Direct/PlaybackFailureCopy.cs`
- Create: `tests/Adapter.Tests/LinkRulesTests.cs`

- [ ] **Step 1: Write the failing adapter tests**

Create `tests/Adapter.Tests/LinkRulesTests.cs`:

```csharp
using Defuse.Domain;
using Defuse.Sources.Direct;

namespace Defuse.Adapter.Tests;

public sealed class LinkRulesTests
{
    [Fact]
    public void Direct_mkv_is_playable()
    {
        var link = LinkClassifier.Classify("https://cdn.example/movie.mkv");
        Assert.Equal(LinkClass.DirectFile, link.Class);
        Assert.True(link.IsPlayableNow);
    }

    [Fact]
    public void Hls_and_dash_are_playable()
    {
        Assert.Equal(LinkClass.Hls, LinkClassifier.Classify("https://cdn.example/a.m3u8").Class);
        Assert.Equal(LinkClass.Dash, LinkClassifier.Classify("https://cdn.example/a.mpd").Class);
    }

    [Fact]
    public void Loopback_mkv_explains_the_local_server()
    {
        var link = LinkClassifier.Classify("http://127.0.0.1:11470/stream.mkv?token=supersecret");
        Assert.Equal(LinkClass.LoopbackDirect, link.Class);
        Assert.True(link.IsLoopback);
        Assert.True(link.SuggestsStremioServer);
        Assert.DoesNotContain("supersecret", link.RedactedDisplay, StringComparison.Ordinal);
        Assert.Contains("this PC", link.UserMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Manifest_deep_link_and_magnet_are_not_videos()
    {
        Assert.Equal(LinkClass.AddonManifest, LinkClassifier.Classify("https://addon.example/manifest.json").Class);
        Assert.Equal(LinkClass.StremioDeepLink, LinkClassifier.Classify("stremio://detail/movie/tt1").Class);
        Assert.Equal(LinkClass.Magnet, LinkClassifier.Classify("magnet:?xt=urn:btih:ABCDEF").Class);
        Assert.False(LinkClassifier.Classify("https://addon.example/manifest.json").IsPlayableNow);
    }

    [Fact]
    public void Playlist_folder_and_unc_are_refused()
    {
        Assert.Equal(LinkClass.PlaylistFile, LinkClassifier.Classify("https://cdn.example/list.m3u").Class);
        Assert.Equal(LinkClass.PlaylistFile, LinkClassifier.Classify(@"C:\Videos\show.strm").Class);
        Assert.Equal(LinkClass.LocalFolder, LinkClassifier.Classify(@"C:\Videos\").Class);
        Assert.Equal(LinkClass.UncPath, LinkClassifier.Classify(@"\\nas\media\a.mkv").Class);
    }

    [Fact]
    public void Netflix_page_is_an_external_shortcut()
    {
        var link = LinkClassifier.Classify("https://www.netflix.com/watch/123");
        Assert.Equal(LinkClass.ExternalPage, link.Class);
        Assert.False(link.IsPlayableNow);
    }

    [Fact]
    public void Extensionless_https_is_unknown_not_a_movie()
    {
        var link = LinkClassifier.Classify("https://cdn.example/get");
        Assert.Equal(LinkClass.UnknownHttp, link.Class);
        Assert.False(link.IsPlayableNow);
    }

    [Fact]
    public void Redactor_drops_userinfo_and_query()
    {
        var uri = new Uri("https://user:supersecret@cdn.example/a.mkv?token=supersecret&id=7");
        var shown = UrlRedactor.Redact(uri);
        Assert.DoesNotContain("supersecret", shown, StringComparison.Ordinal);
        Assert.StartsWith("https://cdn.example/a.mkv", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void Fingerprint_ignores_token_and_keeps_identity_query()
    {
        var a = SourceFingerprint.Compute(new Uri("https://cdn.example/get?id=movie-1&token=aaa"));
        var b = SourceFingerprint.Compute(new Uri("https://cdn.example/get?id=movie-1&token=bbb"));
        var c = SourceFingerprint.Compute(new Uri("https://cdn.example/get?id=movie-2&token=aaa"));
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void Scrubber_removes_token_from_a_log_line()
    {
        var scrubbed = LogScrubber.Scrub("error fetching https://cdn.example/a.mkv?token=supersecretvalue");
        Assert.DoesNotContain("supersecretvalue", scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, true, false, false, "Stremio")]
    [InlineData(true, false, false, false, "this PC")]
    [InlineData(false, false, true, false, "Update this video's link")]
    [InlineData(false, false, false, true, "Update this video's link")]
    [InlineData(false, false, false, false, "still in your library")]
    public void Failure_copy_matches_the_case(bool loopback, bool stremio, bool expires, bool auth, string expected)
    {
        var text = PlaybackFailureCopy.Describe(loopback, stremio, expires, auth);
        Assert.Contains(expected, text, StringComparison.OrdinalIgnoreCase);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```powershell
dotnet test tests/Adapter.Tests/Defuse.Adapter.Tests.csproj
```

Expected: FAIL because the types do not exist.

- [ ] **Step 3: Implement the domain privacy helpers**

Create `src/Domain/SecretQuery.cs`:

```csharp
namespace Defuse.Domain;

public static class SecretQuery
{
    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        "token", "sig", "signature", "expires", "expiry", "exp",
        "password", "auth", "key"
    };

    public static bool IsSecretKey(string key) =>
        Keys.Contains(key) || key.StartsWith("x-amz-", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<(string Key, string Value)> Parse(string? query)
    {
        if (string.IsNullOrEmpty(query))
            return [];
        var text = query.StartsWith('?') ? query[1..] : query;
        var list = new List<(string Key, string Value)>();
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            var rawKey = eq >= 0 ? part[..eq] : part;
            var rawValue = eq >= 0 ? part[(eq + 1)..] : "";
            list.Add((
                Uri.UnescapeDataString(rawKey.Replace('+', ' ')),
                Uri.UnescapeDataString(rawValue.Replace('+', ' '))));
        }

        return list;
    }
}
```

Create `src/Domain/UrlRedactor.cs`:

```csharp
namespace Defuse.Domain;

public static class UrlRedactor
{
    public static string Redact(Uri uri)
    {
        var port = uri.IsDefaultPort ? -1 : uri.Port;
        var safe = new UriBuilder(uri.Scheme, uri.Host, port, uri.AbsolutePath).Uri.AbsoluteUri;
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            return string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) ? safe : safe + "?…";
        return safe;
    }
}
```

Create `src/Domain/SourceFingerprint.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;

namespace Defuse.Domain;

public static class SourceFingerprint
{
    public static string Compute(Uri uri)
    {
        var kept = SecretQuery.Parse(uri.Query)
            .Where(pair => !SecretQuery.IsSecretKey(pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pair => pair.Value, StringComparer.Ordinal)
            .Select(pair => $"{pair.Key}={pair.Value}");
        var canonical = $"{uri.Scheme.ToLowerInvariant()}://{uri.IdnHost.ToLowerInvariant()}{uri.AbsolutePath}?{string.Join("&", kept)}";
        return Hash(canonical);
    }

    public static string ComputeLocal(string path)
    {
        var canonical = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        return Hash(canonical);
    }

    private static string Hash(string canonical)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

Create `src/Domain/LogScrubber.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Defuse.Domain;

public static partial class LogScrubber
{
    [GeneratedRegex(@"https?://[^\s""'<>]+", RegexOptions.IgnoreCase)]
    private static partial Regex Urls();

    [GeneratedRegex(@"(?i)\b(token|sig|signature|password|auth|key)=([^&\s""']+)", RegexOptions.IgnoreCase)]
    private static partial Regex SecretPairs();

    public static string Scrub(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var withoutUrls = Urls().Replace(text, match =>
            Uri.TryCreate(match.Value, UriKind.Absolute, out var uri) ? UrlRedactor.Redact(uri) : "http://…");
        return SecretPairs().Replace(withoutUrls, "$1=…");
    }
}
```

- [ ] **Step 4: Implement the classifier and failure copy**

Create `src/Sources.Direct/LinkClass.cs`:

```csharp
namespace Defuse.Sources.Direct;

public enum LinkClass
{
    DirectFile,
    Hls,
    Dash,
    LoopbackDirect,
    LocalFile,
    LocalFolder,
    UncPath,
    PlaylistFile,
    AddonManifest,
    StremioDeepLink,
    Magnet,
    ExternalPage,
    UnknownHttp,
    Unsupported
}
```

Create `src/Sources.Direct/ClassifiedLink.cs`:

```csharp
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
```

Create `src/Sources.Direct/LinkClassifier.cs`:

```csharp
using Defuse.Domain;

namespace Defuse.Sources.Direct;

public static class LinkClassifier
{
    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".m4v", ".webm", ".ts", ".mov", ".avi", ".wmv",
        ".flv", ".mpg", ".mpeg", ".m2ts"
    };

    private static readonly string[] PageHosts =
    {
        "netflix.com", "disneyplus.com", "primevideo.com", "amazon.com",
        "tv.apple.com", "max.com", "hulu.com", "youtube.com", "youtu.be"
    };

    public static ClassifiedLink Classify(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Refuse(LinkClass.Unsupported, "Enter a link.");

        var text = input.Trim().Trim('"');
        if (text.StartsWith("stremio:", StringComparison.OrdinalIgnoreCase))
            return Refuse(LinkClass.StremioDeepLink, "This is a Stremio app link, not a video file.");
        if (text.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            return Refuse(LinkClass.Magnet, "Torrent links are not playable in this release.");
        if (text.StartsWith(@"\\", StringComparison.Ordinal))
            return Refuse(LinkClass.UncPath, "Network folders are not available in this release.");

        if (LooksLikeLocalPath(text))
        {
            if (text.EndsWith('\\') || text.EndsWith('/'))
                return Refuse(LinkClass.LocalFolder, "Folder import is not available in this release.");
            var ext = Path.GetExtension(text);
            if (ext.Equals(".strm", StringComparison.OrdinalIgnoreCase) || ext.Equals(".m3u", StringComparison.OrdinalIgnoreCase))
                return Refuse(LinkClass.PlaylistFile, "Playlists and .strm files are not available in this release.");
            if (!VideoExtensions.Contains(ext))
                return Refuse(LinkClass.Unsupported, "That local file is not a video type this release plays.");
            return new ClassifiedLink(
                LinkClass.LocalFile, null, text, true, false, false, false,
                "", Path.GetFileName(text));
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri))
            return Refuse(LinkClass.Unsupported, "This does not look like a playable video link.");
        if (uri.Scheme == Uri.UriSchemeFile)
            return Classify(uri.LocalPath);
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Refuse(LinkClass.Unsupported, "This link type is not playable in this release.");

        var loopback = IsLoopback(uri);
        var stremio = loopback && uri.Port == 11470;
        var mayExpire = SecretQuery.Parse(uri.Query).Any(pair => SecretQuery.IsSecretKey(pair.Key));
        var redacted = UrlRedactor.Redact(uri);
        var fileName = Path.GetFileName(uri.AbsolutePath);
        var extName = Path.GetExtension(uri.AbsolutePath);

        if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
            return new ClassifiedLink(LinkClass.AddonManifest, uri, null, false, false, loopback, stremio,
                "This is an addon link. Provider setup is not available yet.", redacted);
        if (extName.Equals(".m3u", StringComparison.OrdinalIgnoreCase) || extName.Equals(".strm", StringComparison.OrdinalIgnoreCase))
            return new ClassifiedLink(LinkClass.PlaylistFile, uri, null, false, mayExpire, loopback, stremio,
                "Playlists and .strm files are not available in this release.", redacted);

        var kind = KindForExtension(extName, loopback);
        if (kind is not null)
        {
            return new ClassifiedLink(kind.Value, uri, null, true, mayExpire, loopback, stremio,
                PlayableMessage(loopback, stremio, mayExpire), redacted);
        }

        if (IsPageHost(uri.Host))
            return new ClassifiedLink(LinkClass.ExternalPage, uri, null, false, false, loopback, stremio,
                "This is a web page. You can save a shortcut. This release will not play it.", redacted);

        return new ClassifiedLink(LinkClass.UnknownHttp, uri, null, false, mayExpire, loopback, stremio,
            "No video file type was detected. You can try to play it, or save a shortcut.", redacted);
    }

    private static LinkClass? KindForExtension(string ext, bool loopback)
    {
        if (ext.Equals(".m3u8", StringComparison.OrdinalIgnoreCase))
            return LinkClass.Hls;
        if (ext.Equals(".mpd", StringComparison.OrdinalIgnoreCase))
            return LinkClass.Dash;
        if (VideoExtensions.Contains(ext))
            return loopback ? LinkClass.LoopbackDirect : LinkClass.DirectFile;
        return null;
    }

    private static string PlayableMessage(bool loopback, bool stremio, bool mayExpire)
    {
        if (stremio)
            return "This plays only while Stremio is open on this PC.";
        if (loopback)
            return "This plays only while the program serving it on this PC is running.";
        if (mayExpire)
            return "This link may expire. The title and your place are saved separately.";
        return "";
    }

    private static bool IsPageHost(string host)
    {
        foreach (var page in PageHosts)
        {
            if (host.Equals(page, StringComparison.OrdinalIgnoreCase) || host.EndsWith("." + page, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        || uri.Host.Equals("127.0.0.1", StringComparison.Ordinal)
        || uri.Host.Equals("::1", StringComparison.Ordinal);

    private static bool LooksLikeLocalPath(string text) =>
        text.Length >= 3 && char.IsLetter(text[0]) && text[1] == ':' && (text[2] == '\\' || text[2] == '/');

    private static ClassifiedLink Refuse(LinkClass kind, string message) =>
        new(kind, null, null, false, false, false, false, message, "");
}
```

Create `src/Sources.Direct/PlaybackFailureCopy.cs`:

```csharp
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
```

- [ ] **Step 5: Run the tests and confirm they pass**

```powershell
dotnet test tests/Adapter.Tests/Defuse.Adapter.Tests.csproj
```

Expected: PASS. If `UrlRedactor` keeps a trailing slash that the assertion rejects, compare the host and path with `Assert.Contains("/a.mkv", shown)` and keep the secret assertion.

- [ ] **Step 6: Commit when commits are requested**

```powershell
git add src/Domain src/Sources.Direct tests/Adapter.Tests
git commit -m "feat: classify links and keep tokens out of display text"
```

## Task 4: Resume policy and progress interval

**Files:**
- Create: `src/Domain/LibraryProfile.cs`
- Create: `src/Domain/TitleNormalizer.cs`
- Create: `src/Domain/PlaybackProgress.cs`
- Create: `src/Domain/ResumePolicy.cs`
- Create: `src/Domain/ProgressTracker.cs`
- Create: `tests/Domain.Tests/ProgressRulesTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Domain.Tests/ProgressRulesTests.cs`:

```csharp
using Defuse.Domain;

namespace Defuse.Domain.Tests;

public sealed class ProgressRulesTests
{
    private static PlaybackProgress Row(long positionMs, long durationMs, bool completed = false) =>
        new(LibraryProfile.Id, Guid.NewGuid(), Guid.NewGuid(), positionMs, durationMs, completed, DateTimeOffset.UnixEpoch, Guid.NewGuid());

    [Fact]
    public void Under_thirty_seconds_resumes_without_a_prompt()
    {
        var decision = ResumePolicy.Evaluate(Row(20_000, 3_600_000), new PlaybackCapabilities(true, false));
        Assert.Equal(ResumeKind.ResumeQuiet, decision.Kind);
        Assert.Equal(20_000, decision.PositionMs);
    }

    [Fact]
    public void Forty_two_minutes_asks_before_playing()
    {
        var position = ((42 * 60) + 10) * 1000;
        var decision = ResumePolicy.Evaluate(Row(position, 7_200_000), new PlaybackCapabilities(true, false));
        Assert.Equal(ResumeKind.OfferResume, decision.Kind);
        Assert.Equal(position, decision.PositionMs);
    }

    [Fact]
    public void Ninety_five_percent_is_finished()
    {
        var decision = ResumePolicy.Evaluate(Row(95_000, 100_000), new PlaybackCapabilities(true, false));
        Assert.Equal(ResumeKind.Completed, decision.Kind);
    }

    [Fact]
    public void Unseekable_video_does_not_offer_an_exact_place()
    {
        var decision = ResumePolicy.Evaluate(Row(50_000, 0), new PlaybackCapabilities(false, true));
        Assert.Equal(ResumeKind.Unseekable, decision.Kind);
        Assert.Contains("Live", decision.Explanation, StringComparison.Ordinal);
    }

    [Fact]
    public void Tracker_flushes_on_start_and_then_every_five_seconds()
    {
        var tracker = new ProgressTracker();
        var start = new DateTimeOffset(2026, 9, 26, 0, 0, 0, TimeSpan.Zero);
        var media = Guid.NewGuid();
        var version = Guid.NewGuid();
        var source = Guid.NewGuid();
        var first = tracker.Start(LibraryProfile.Id, media, version, source, 0, 0, start);
        Assert.Equal(0, first.PositionMs);

        Assert.Null(tracker.Observe(4_000, 100_000, start.AddSeconds(4)));
        var flushed = tracker.Observe(6_000, 100_000, start.AddSeconds(5));
        Assert.NotNull(flushed);
        Assert.Equal(6_000, flushed!.PositionMs);
        Assert.Equal(media, flushed.MediaId);
    }

    [Fact]
    public void Forced_flush_keeps_a_seek()
    {
        var tracker = new ProgressTracker();
        var start = DateTimeOffset.UnixEpoch;
        tracker.Start(LibraryProfile.Id, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0, 100_000, start);
        var row = tracker.Flush(2_530_000, 7_200_000, start.AddSeconds(1));
        Assert.Equal(2_530_000, row.PositionMs);
        Assert.False(row.Completed);
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```powershell
dotnet test tests/Domain.Tests/Defuse.Domain.Tests.csproj
```

Expected: FAIL because the types do not exist.

- [ ] **Step 3: Implement the rules**

Create `src/Domain/LibraryProfile.cs`:

```csharp
namespace Defuse.Domain;

public static class LibraryProfile
{
    public static readonly Guid Id = new("8f4e2c10-6b3a-4d77-9c1e-2a5b7d9e0f11");
    public const string Name = "Library";
}
```

Create `src/Domain/TitleNormalizer.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Defuse.Domain;

public static partial class TitleNormalizer
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static string Normalize(string title) =>
        Whitespace().Replace(title.Trim().ToLowerInvariant(), " ");
}
```

Create `src/Domain/PlaybackProgress.cs`:

```csharp
namespace Defuse.Domain;

public sealed record PlaybackProgress(
    Guid ProfileId,
    Guid MediaId,
    Guid VersionId,
    long PositionMs,
    long DurationMs,
    bool Completed,
    DateTimeOffset UpdatedAt,
    Guid SourceId);

public readonly record struct PlaybackCapabilities(bool Seekable, bool IsLive);

public enum ResumeKind
{
    FromStart,
    ResumeQuiet,
    OfferResume,
    Completed,
    Unseekable
}

public readonly record struct ResumeDecision(ResumeKind Kind, long PositionMs, string Explanation)
{
    public static ResumeDecision FromStart() => new(ResumeKind.FromStart, 0, "Play from the start.");
    public static ResumeDecision ResumeQuiet(long positionMs) => new(ResumeKind.ResumeQuiet, positionMs, "Resume.");
    public static ResumeDecision OfferResume(long positionMs) => new(ResumeKind.OfferResume, positionMs, "Resume from where you left off.");
    public static ResumeDecision Completed() => new(ResumeKind.Completed, 0, "Start over. This was already finished.");
    public static ResumeDecision Unseekable(string explanation) => new(ResumeKind.Unseekable, 0, explanation);
}
```

Create `src/Domain/ResumePolicy.cs`:

```csharp
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
        if (saved is null || saved.PositionMs <= 0)
            return ResumeDecision.FromStart();
        if (saved.DurationMs <= 0)
            return ResumeDecision.Unseekable("This video can't resume at an exact time.");
        if (saved.Completed || IsComplete(saved.PositionMs, saved.DurationMs))
            return ResumeDecision.Completed();
        if (saved.PositionMs < ResumeAfterMs)
            return ResumeDecision.ResumeQuiet(saved.PositionMs);
        return ResumeDecision.OfferResume(saved.PositionMs);
    }
}
```

Create `src/Domain/ProgressTracker.cs`:

```csharp
namespace Defuse.Domain;

public sealed class ProgressTracker
{
    public static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(5);
    private PlaybackProgress? _current;
    private DateTimeOffset _lastFlush;

    public PlaybackProgress Start(
        Guid profileId,
        Guid mediaId,
        Guid versionId,
        Guid sourceId,
        long positionMs,
        long durationMs,
        DateTimeOffset now)
    {
        _current = new PlaybackProgress(
            profileId, mediaId, versionId, positionMs, durationMs,
            ResumePolicy.IsComplete(positionMs, durationMs), now, sourceId);
        _lastFlush = now;
        return _current;
    }

    public PlaybackProgress? Observe(long positionMs, long durationMs, DateTimeOffset now)
    {
        var updated = Update(positionMs, durationMs, now);
        if (now - _lastFlush < FlushInterval)
            return null;
        _lastFlush = now;
        return updated;
    }

    public PlaybackProgress Flush(long positionMs, long durationMs, DateTimeOffset now)
    {
        var updated = Update(positionMs, durationMs, now);
        _lastFlush = now;
        return updated;
    }

    private PlaybackProgress Update(long positionMs, long durationMs, DateTimeOffset now)
    {
        if (_current is null)
            throw new InvalidOperationException("Playback has not started.");
        _current = _current with
        {
            PositionMs = positionMs,
            DurationMs = durationMs,
            UpdatedAt = now,
            Completed = ResumePolicy.IsComplete(positionMs, durationMs)
        };
        return _current;
    }
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

```powershell
dotnet test tests/Domain.Tests/Defuse.Domain.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit when commits are requested**

```powershell
git add src/Domain tests/Domain.Tests
git commit -m "feat: decide resume and limit progress loss to five seconds"
```

## Task 5: SQLite library and protected locators

**Files:**
- Create: `src/Application/LibraryContracts.cs`
- Create: `src/Persistence/DpapiSecretProtector.cs`
- Create: `src/Persistence/SqliteLibraryStore.cs`
- Create: `tests/Persistence.Tests/LibraryStoreTests.cs`
- Create: `tests/Persistence.Tests/XorSecretProtector.cs`

- [ ] **Step 1: Write the store contract**

Create `src/Application/LibraryContracts.cs`:

```csharp
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
    bool Completed);

public interface ILibraryStore
{
    void Initialize();
    Guid? FindMediaIdByFingerprint(string fingerprint);
    IReadOnlyList<TitleMatch> FindTitleMatches(string normalizedTitle);
    void CreateTitle(NewTitle title);
    void UpdateLocator(Guid sourceId, NewTitle replacement);
    void AddPreferredSource(Guid mediaId, NewTitle source);
    PlayTarget? GetPlayTarget(Guid mediaId);
    void UpsertProgress(PlaybackProgress progress);
    PlaybackProgress? GetProgress(Guid profileId, Guid mediaId);
    void ClearProgress(Guid profileId, Guid mediaId);
    IReadOnlyList<ContinueItem> ListContinueWatching(Guid profileId);
    IReadOnlyList<LibraryItem> ListLibrary();
    void SetSeekable(Guid sourceId, bool seekable);
    void SetPoster(Guid mediaId, string cachePath);
    IReadOnlyList<string> DeleteTitle(Guid mediaId);
    bool GetClipboardWatch();
    void SetClipboardWatch(bool enabled);
}
```

`NewTitle` is used both for a new media row and for a locator replacement. Callers fill media and version ids even when `UpdateLocator` only reads the source fields. `UpdateLocator` uses `SourceId`, locator, redacted text, fingerprint, expiry, loopback, Stremio hint, user message, and subtitle fields.

- [ ] **Step 2: Write the failing persistence tests**

Create `tests/Persistence.Tests/XorSecretProtector.cs`:

```csharp
using System.Text;
using Defuse.Application;

namespace Defuse.Persistence.Tests;

public sealed class XorSecretProtector : ISecretProtector
{
    public byte[] Protect(string plaintext) =>
        Encoding.UTF8.GetBytes(plaintext).Select(b => (byte)(b ^ 0x5A)).ToArray();

    public string Unprotect(byte[] payload) =>
        Encoding.UTF8.GetString(payload.Select(b => (byte)(b ^ 0x5A)).ToArray());
}
```

Create `tests/Persistence.Tests/LibraryStoreTests.cs`:

```csharp
using Defuse.Application;
using Defuse.Domain;
using Defuse.Persistence;

namespace Defuse.Persistence.Tests;

public sealed class LibraryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "defuse-tests", Guid.NewGuid().ToString("N"));
    private readonly SqliteLibraryStore _store;

    public LibraryStoreTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new SqliteLibraryStore(Path.Combine(_directory, "library.db"), new XorSecretProtector());
        _store.Initialize();
    }

    [Fact]
    public void Initialize_twice_keeps_the_saved_title()
    {
        _store.CreateTitle(Sample("https://cdn.example/a.mkv?token=supersecretvalue"));
        _store.Initialize();
        Assert.Single(_store.ListLibrary());
    }

    [Fact]
    public void Database_file_does_not_contain_the_token()
    {
        _store.CreateTitle(Sample("https://cdn.example/a.mkv?token=supersecretvalue"));
        var bytes = File.ReadAllBytes(Path.Combine(_directory, "library.db"));
        var latin1 = System.Text.Encoding.Latin1.GetString(bytes);
        Assert.DoesNotContain("supersecretvalue", latin1, StringComparison.Ordinal);
        var target = _store.GetPlayTarget(_store.ListLibrary()[0].MediaId);
        Assert.Equal("https://cdn.example/a.mkv?token=supersecretvalue", target!.Locator);
    }

    [Fact]
    public void Replace_locator_keeps_position_and_media_id()
    {
        var title = Sample("https://cdn.example/a.mkv?token=old");
        _store.CreateTitle(title);
        var position = ((42 * 60) + 10) * 1000L;
        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, title.MediaId, title.VersionId, position, 7_200_000, false, DateTimeOffset.UnixEpoch, title.SourceId));

        var replacement = title with
        {
            LocatorPlaintext = "https://cdn.example/a.mkv?token=new",
            LocatorRedacted = "https://cdn.example/a.mkv?…",
            Fingerprint = SourceFingerprint.Compute(new Uri("https://cdn.example/a.mkv?token=new"))
        };
        _store.UpdateLocator(title.SourceId, replacement);

        var progress = _store.GetProgress(LibraryProfile.Id, title.MediaId);
        var target = _store.GetPlayTarget(title.MediaId);
        Assert.Equal(position, progress!.PositionMs);
        Assert.Equal(title.MediaId, target!.MediaId);
        Assert.Equal("https://cdn.example/a.mkv?token=new", target.Locator);
    }

    [Fact]
    public void Continue_watching_lists_a_midpoint_and_hides_a_finished_item()
    {
        var title = Sample("https://cdn.example/b.mp4");
        _store.CreateTitle(title);
        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, title.MediaId, title.VersionId, 40_000, 100_000, false, DateTimeOffset.UnixEpoch, title.SourceId));
        Assert.Single(_store.ListContinueWatching(LibraryProfile.Id));

        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, title.MediaId, title.VersionId, 96_000, 100_000, true, DateTimeOffset.UnixEpoch, title.SourceId));
        Assert.Empty(_store.ListContinueWatching(LibraryProfile.Id));
    }

    [Fact]
    public void Clear_progress_and_delete_keep_the_other_title()
    {
        var first = Sample("https://cdn.example/one.mp4");
        var second = Sample("https://cdn.example/two.mp4");
        _store.CreateTitle(first);
        _store.CreateTitle(second);
        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, first.MediaId, first.VersionId, 40_000, 100_000, false, DateTimeOffset.UnixEpoch, first.SourceId));
        _store.ClearProgress(LibraryProfile.Id, first.MediaId);
        Assert.Null(_store.GetProgress(LibraryProfile.Id, first.MediaId));
        _store.DeleteTitle(first.MediaId);
        Assert.Single(_store.ListLibrary());
    }

    [Fact]
    public void Thousand_titles_list_back()
    {
        for (var i = 0; i < 1000; i++)
            _store.CreateTitle(Sample($"https://cdn.example/v/{i}.mp4"));
        Assert.Equal(1000, _store.ListLibrary().Count);
    }

    [Fact]
    public void Newer_schema_is_refused()
    {
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(_directory, "library.db")}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version = 99;";
        command.ExecuteNonQuery();
        var error = Assert.Throws<InvalidOperationException>(() => _store.Initialize());
        Assert.Contains("newer Defuse", error.Message, StringComparison.Ordinal);
    }

    public void Dispose() => _store.Dispose();

    private static NewTitle Sample(string url)
    {
        var uri = new Uri(url);
        var id = Guid.NewGuid();
        return new NewTitle(
            id, Guid.NewGuid(), Guid.NewGuid(), "video", "Sample", "sample", null, null, null, null,
            "DirectFile", url, UrlRedactor.Redact(uri), SourceFingerprint.Compute(uri),
            true, false, false, "", null, null);
    }
}
```

- [ ] **Step 3: Run the tests and confirm they fail**

```powershell
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj
```

Expected: FAIL because `SqliteLibraryStore` does not exist.

- [ ] **Step 4: Implement DPAPI and the store**

Create `src/Persistence/DpapiSecretProtector.cs`:

```csharp
using System.Security.Cryptography;
using System.Text;
using Defuse.Application;

namespace Defuse.Persistence;

public sealed class DpapiSecretProtector : ISecretProtector
{
    public byte[] Protect(string plaintext)
    {
        var bytes = Encoding.UTF8.GetBytes(plaintext);
        try
        {
            return ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string Unprotect(byte[] payload)
    {
        var bytes = ProtectedData.Unprotect(payload, optionalEntropy: null, DataProtectionScope.CurrentUser);
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
```

If `Newer_schema_is_refused` cannot see `Microsoft.Data.Sqlite`, add package `Microsoft.Data.Sqlite` version `10.0.12` to the test project.

Create `src/Persistence/SqliteLibraryStore.cs`:

```csharp
using Defuse.Application;
using Defuse.Domain;
using Microsoft.Data.Sqlite;

namespace Defuse.Persistence;

public sealed class SqliteLibraryStore : ILibraryStore, IDisposable
{
    private readonly string _path;
    private readonly ISecretProtector _protector;

    public SqliteLibraryStore(string databasePath, ISecretProtector protector)
    {
        _path = databasePath;
        _protector = protector;
    }

    public void Dispose()
    {
    }

    public void Initialize()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using var connection = Open();
        using (var wal = connection.CreateCommand())
        {
            wal.CommandText = "PRAGMA journal_mode=WAL;";
            wal.ExecuteNonQuery();
        }

        var version = UserVersion(connection);
        if (version > 1)
            throw new InvalidOperationException($"This library was created by a newer Defuse.");
        if (version == 1)
            return;

        using var tx = connection.BeginTransaction();
        using (var schema = connection.CreateCommand())
        {
            schema.Transaction = tx;
            schema.CommandText = Schema;
            schema.ExecuteNonQuery();
        }

        using (var profile = connection.CreateCommand())
        {
            profile.Transaction = tx;
            profile.CommandText = "INSERT INTO profiles (id, name) VALUES ($id, $name);";
            profile.Parameters.AddWithValue("$id", LibraryProfile.Id.ToString("D"));
            profile.Parameters.AddWithValue("$name", LibraryProfile.Name);
            profile.ExecuteNonQuery();
        }

        tx.Commit();
        using var stamp = connection.CreateCommand();
        stamp.CommandText = "PRAGMA user_version = 1;";
        stamp.ExecuteNonQuery();
    }

    public Guid? FindMediaIdByFingerprint(string fingerprint)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT v.media_id
            FROM sources s
            JOIN media_versions v ON v.id = s.version_id
            WHERE s.fingerprint = $fp
            """;
        command.Parameters.AddWithValue("$fp", fingerprint);
        var value = command.ExecuteScalar();
        return value is string text ? Guid.Parse(text) : null;
    }

    public IReadOnlyList<TitleMatch> FindTitleMatches(string normalizedTitle)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, title FROM media_items WHERE normalized_title = $n ORDER BY created_at;";
        command.Parameters.AddWithValue("$n", normalizedTitle);
        using var reader = command.ExecuteReader();
        var list = new List<TitleMatch>();
        while (reader.Read())
            list.Add(new TitleMatch(Guid.Parse(reader.GetString(0)), reader.GetString(1)));
        return list;
    }

    public void CreateTitle(NewTitle title)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        EnsureFingerprintAvailable(connection, tx, title.Fingerprint, null);
        InsertMedia(connection, tx, title);
        InsertVersion(connection, tx, title);
        InsertSource(connection, tx, title, preferred: true);
        ReplaceSubtitle(connection, tx, title.VersionId, title);
        tx.Commit();
    }

    public void UpdateLocator(Guid sourceId, NewTitle replacement)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var versionId = VersionForSource(connection, tx, sourceId)
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        EnsureFingerprintAvailable(connection, tx, replacement.Fingerprint, sourceId);
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            UPDATE sources
            SET kind = $kind,
                locator_protected = $locator,
                locator_redacted = $redacted,
                fingerprint = $fp,
                may_expire = $expire,
                is_loopback = $loop,
                stremio_hint = $stremio,
                dependency_note = $note
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$kind", replacement.Kind);
        command.Parameters.Add("$locator", SqliteType.Blob).Value = _protector.Protect(replacement.LocatorPlaintext);
        command.Parameters.AddWithValue("$redacted", replacement.LocatorRedacted);
        command.Parameters.AddWithValue("$fp", replacement.Fingerprint);
        command.Parameters.AddWithValue("$expire", replacement.MayExpire ? 1 : 0);
        command.Parameters.AddWithValue("$loop", replacement.IsLoopback ? 1 : 0);
        command.Parameters.AddWithValue("$stremio", replacement.SuggestsStremioServer ? 1 : 0);
        command.Parameters.AddWithValue("$note", (object?)replacement.UserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$id", sourceId.ToString("D"));
        command.ExecuteNonQuery();
        ReplaceSubtitle(connection, tx, versionId, replacement);
        tx.Commit();
    }

    public void AddPreferredSource(Guid mediaId, NewTitle source)
    {
        using var connection = Open();
        using var tx = connection.BeginTransaction();
        var versionId = VersionForMedia(connection, tx, mediaId)
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        EnsureFingerprintAvailable(connection, tx, source.Fingerprint, null);
        using (var clear = connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "UPDATE sources SET preferred = 0 WHERE version_id = $version;";
            clear.Parameters.AddWithValue("$version", versionId.ToString("D"));
            clear.ExecuteNonQuery();
        }

        InsertSource(connection, tx, source with { VersionId = versionId }, preferred: true);
        ReplaceSubtitle(connection, tx, versionId, source);
        tx.Commit();
    }

    public PlayTarget? GetPlayTarget(Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.title, v.id, s.id, s.locator_protected, s.locator_redacted, s.seekable,
                   s.is_loopback, s.may_expire, s.stremio_hint, s.dependency_note, sub.locator_protected,
                   p.version_id, p.position_ms, p.duration_ms, p.completed, p.updated_at, p.last_source_id
            FROM media_items m
            JOIN media_versions v ON v.media_id = m.id
            JOIN sources s ON s.version_id = v.id AND s.preferred = 1
            LEFT JOIN subtitles sub ON sub.version_id = v.id
            LEFT JOIN playback_state p ON p.media_id = m.id AND p.profile_id = $profile
            WHERE m.id = $id;
            """;
        command.Parameters.AddWithValue("$profile", LibraryProfile.Id.ToString("D"));
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;

        var versionId = Guid.Parse(reader.GetString(1));
        var sourceId = Guid.Parse(reader.GetString(2));
        PlaybackProgress? progress = reader.IsDBNull(11)
            ? null
            : new PlaybackProgress(
                LibraryProfile.Id,
                mediaId,
                Guid.Parse(reader.GetString(11)),
                reader.GetInt64(12),
                reader.GetInt64(13),
                reader.GetInt64(14) == 1,
                DateTimeOffset.Parse(reader.GetString(15)),
                Guid.Parse(reader.GetString(16)));
        return new PlayTarget(
            mediaId,
            versionId,
            sourceId,
            reader.GetString(0),
            _protector.Unprotect((byte[])reader.GetValue(3)),
            reader.IsDBNull(10) ? null : _protector.Unprotect((byte[])reader.GetValue(10)),
            reader.IsDBNull(5) ? null : reader.GetInt64(5) == 1,
            reader.GetInt64(6) == 1,
            reader.GetInt64(7) == 1,
            reader.GetInt64(8) == 1,
            reader.GetString(4),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            progress);
    }

    public void UpsertProgress(PlaybackProgress progress)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO playback_state
                (profile_id, media_id, version_id, position_ms, duration_ms, completed, updated_at, last_source_id)
            VALUES ($profile, $media, $version, $position, $duration, $completed, $updated, $source)
            ON CONFLICT(profile_id, media_id) DO UPDATE SET
                version_id = excluded.version_id,
                position_ms = excluded.position_ms,
                duration_ms = excluded.duration_ms,
                completed = excluded.completed,
                updated_at = excluded.updated_at,
                last_source_id = excluded.last_source_id;
            """;
        command.Parameters.AddWithValue("$profile", progress.ProfileId.ToString("D"));
        command.Parameters.AddWithValue("$media", progress.MediaId.ToString("D"));
        command.Parameters.AddWithValue("$version", progress.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$position", progress.PositionMs);
        command.Parameters.AddWithValue("$duration", progress.DurationMs);
        command.Parameters.AddWithValue("$completed", progress.Completed ? 1 : 0);
        command.Parameters.AddWithValue("$updated", progress.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$source", progress.SourceId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public PlaybackProgress? GetProgress(Guid profileId, Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT version_id, position_ms, duration_ms, completed, updated_at, last_source_id
            FROM playback_state
            WHERE profile_id = $profile AND media_id = $media;
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        using var reader = command.ExecuteReader();
        if (!reader.Read())
            return null;
        return new PlaybackProgress(
            profileId, mediaId, Guid.Parse(reader.GetString(0)), reader.GetInt64(1), reader.GetInt64(2),
            reader.GetInt64(3) == 1, DateTimeOffset.Parse(reader.GetString(4)), Guid.Parse(reader.GetString(5)));
    }

    public void ClearProgress(Guid profileId, Guid mediaId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM playback_state WHERE profile_id = $profile AND media_id = $media;";
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        command.Parameters.AddWithValue("$media", mediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<ContinueItem> ListContinueWatching(Guid profileId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.id, m.title, p.position_ms, p.duration_ms, s.locator_redacted, a.cache_path
            FROM playback_state p
            JOIN media_items m ON m.id = p.media_id
            JOIN sources s ON s.id = p.last_source_id
            LEFT JOIN artwork a ON a.media_id = m.id
            WHERE p.profile_id = $profile
              AND p.completed = 0
              AND p.position_ms >= 30000
              AND p.duration_ms > 0
              AND p.position_ms < CAST(p.duration_ms * 0.95 AS INTEGER)
              AND COALESCE(s.seekable, 1) = 1
            ORDER BY p.updated_at DESC;
            """;
        command.Parameters.AddWithValue("$profile", profileId.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<ContinueItem>();
        while (reader.Read())
        {
            list.Add(new ContinueItem(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetInt64(2), reader.GetInt64(3),
                reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5)));
        }

        return list;
    }

    public IReadOnlyList<LibraryItem> ListLibrary()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT m.id, m.title, m.type, s.locator_redacted, a.cache_path, p.position_ms, p.completed
            FROM media_items m
            JOIN media_versions v ON v.media_id = m.id
            JOIN sources s ON s.version_id = v.id AND s.preferred = 1
            LEFT JOIN artwork a ON a.media_id = m.id
            LEFT JOIN playback_state p ON p.media_id = m.id AND p.profile_id = $profile
            ORDER BY m.created_at DESC;
            """;
        command.Parameters.AddWithValue("$profile", LibraryProfile.Id.ToString("D"));
        using var reader = command.ExecuteReader();
        var list = new List<LibraryItem>();
        while (reader.Read())
        {
            list.Add(new LibraryItem(
                Guid.Parse(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetInt64(5),
                !reader.IsDBNull(6) && reader.GetInt64(6) == 1));
        }

        return list;
    }

    public void SetSeekable(Guid sourceId, bool seekable)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE sources SET seekable = $seekable WHERE id = $id;";
        command.Parameters.AddWithValue("$seekable", seekable ? 1 : 0);
        command.Parameters.AddWithValue("$id", sourceId.ToString("D"));
        command.ExecuteNonQuery();
    }

    public void SetPoster(Guid mediaId, string cachePath)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO artwork (media_id, role, cache_path, manual_override)
            VALUES ($id, 'poster', $path, 1)
            ON CONFLICT(media_id) DO UPDATE SET cache_path = excluded.cache_path, manual_override = 1;
            """;
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        command.Parameters.AddWithValue("$path", cachePath);
        command.ExecuteNonQuery();
    }

    public IReadOnlyList<string> DeleteTitle(Guid mediaId)
    {
        using var connection = Open();
        var paths = new List<string>();
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT cache_path FROM artwork WHERE media_id = $id;";
            read.Parameters.AddWithValue("$id", mediaId.ToString("D"));
            using var reader = read.ExecuteReader();
            while (reader.Read())
                paths.Add(reader.GetString(0));
        }

        using var tx = connection.BeginTransaction();
        foreach (var sql in new[]
        {
            "DELETE FROM playback_state WHERE media_id = $id;",
            "DELETE FROM artwork WHERE media_id = $id;",
            "DELETE FROM subtitles WHERE version_id IN (SELECT id FROM media_versions WHERE media_id = $id);",
            "DELETE FROM sources WHERE version_id IN (SELECT id FROM media_versions WHERE media_id = $id);",
            "DELETE FROM media_versions WHERE media_id = $id;",
            "DELETE FROM media_items WHERE id = $id;"
        })
        {
            using var command = connection.CreateCommand();
            command.Transaction = tx;
            command.CommandText = sql;
            command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
            command.ExecuteNonQuery();
        }

        tx.Commit();
        return paths;
    }

    public bool GetClipboardWatch()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM settings WHERE key = 'clipboard.watch';";
        return command.ExecuteScalar() as string == "1";
    }

    public void SetClipboardWatch(bool enabled)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO settings (key, value) VALUES ('clipboard.watch', $v)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$v", enabled ? "1" : "0");
        command.ExecuteNonQuery();
    }

    private void EnsureFingerprintAvailable(SqliteConnection connection, SqliteTransaction tx, string fingerprint, Guid? exceptSourceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT id FROM sources WHERE fingerprint = $fp;";
        command.Parameters.AddWithValue("$fp", fingerprint);
        var existing = command.ExecuteScalar() as string;
        if (existing is not null && existing != exceptSourceId?.ToString("D"))
            throw new InvalidOperationException("That link is already saved on another title.");
    }

    private void InsertMedia(SqliteConnection connection, SqliteTransaction tx, NewTitle title)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO media_items
                (id, type, title, normalized_title, year, show_title, season_number, episode_number, created_at)
            VALUES ($id, $type, $title, $norm, $year, $show, $season, $episode, $created);
            """;
        command.Parameters.AddWithValue("$id", title.MediaId.ToString("D"));
        command.Parameters.AddWithValue("$type", title.Type);
        command.Parameters.AddWithValue("$title", title.Title);
        command.Parameters.AddWithValue("$norm", title.NormalizedTitle);
        command.Parameters.AddWithValue("$year", (object?)title.Year ?? DBNull.Value);
        command.Parameters.AddWithValue("$show", (object?)title.ShowTitle ?? DBNull.Value);
        command.Parameters.AddWithValue("$season", (object?)title.SeasonNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$episode", (object?)title.EpisodeNumber ?? DBNull.Value);
        command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private void InsertVersion(SqliteConnection connection, SqliteTransaction tx, NewTitle title)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "INSERT INTO media_versions (id, media_id) VALUES ($id, $media);";
        command.Parameters.AddWithValue("$id", title.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$media", title.MediaId.ToString("D"));
        command.ExecuteNonQuery();
    }

    private void InsertSource(SqliteConnection connection, SqliteTransaction tx, NewTitle title, bool preferred)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = """
            INSERT INTO sources
                (id, version_id, kind, locator_protected, locator_redacted, fingerprint, may_expire, is_loopback, stremio_hint, dependency_note, preferred)
            VALUES ($id, $version, $kind, $locator, $redacted, $fp, $expire, $loop, $stremio, $note, $preferred);
            """;
        command.Parameters.AddWithValue("$id", title.SourceId.ToString("D"));
        command.Parameters.AddWithValue("$version", title.VersionId.ToString("D"));
        command.Parameters.AddWithValue("$kind", title.Kind);
        command.Parameters.Add("$locator", SqliteType.Blob).Value = _protector.Protect(title.LocatorPlaintext);
        command.Parameters.AddWithValue("$redacted", title.LocatorRedacted);
        command.Parameters.AddWithValue("$fp", title.Fingerprint);
        command.Parameters.AddWithValue("$expire", title.MayExpire ? 1 : 0);
        command.Parameters.AddWithValue("$loop", title.IsLoopback ? 1 : 0);
        command.Parameters.AddWithValue("$stremio", title.SuggestsStremioServer ? 1 : 0);
        command.Parameters.AddWithValue("$note", (object?)title.UserMessage ?? DBNull.Value);
        command.Parameters.AddWithValue("$preferred", preferred ? 1 : 0);
        command.ExecuteNonQuery();
    }

    private void ReplaceSubtitle(SqliteConnection connection, SqliteTransaction tx, Guid versionId, NewTitle title)
    {
        using (var delete = connection.CreateCommand())
        {
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM subtitles WHERE version_id = $version;";
            delete.Parameters.AddWithValue("$version", versionId.ToString("D"));
            delete.ExecuteNonQuery();
        }

        if (title.SubtitlePlaintext is null)
            return;

        using var insert = connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = """
            INSERT INTO subtitles (id, version_id, locator_protected, locator_redacted)
            VALUES ($id, $version, $blob, $redacted);
            """;
        insert.Parameters.AddWithValue("$id", Guid.NewGuid().ToString("D"));
        insert.Parameters.AddWithValue("$version", versionId.ToString("D"));
        insert.Parameters.Add("$blob", SqliteType.Blob).Value = _protector.Protect(title.SubtitlePlaintext);
        insert.Parameters.AddWithValue("$redacted", title.SubtitleRedacted ?? "");
        insert.ExecuteNonQuery();
    }

    private static Guid? VersionForSource(SqliteConnection connection, SqliteTransaction tx, Guid sourceId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT version_id FROM sources WHERE id = $id;";
        command.Parameters.AddWithValue("$id", sourceId.ToString("D"));
        return command.ExecuteScalar() is string text ? Guid.Parse(text) : null;
    }

    private static Guid? VersionForMedia(SqliteConnection connection, SqliteTransaction tx, Guid mediaId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = tx;
        command.CommandText = "SELECT id FROM media_versions WHERE media_id = $id LIMIT 1;";
        command.Parameters.AddWithValue("$id", mediaId.ToString("D"));
        return command.ExecuteScalar() is string text ? Guid.Parse(text) : null;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static int UserVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    private const string Schema = """
        CREATE TABLE profiles (
          id TEXT PRIMARY KEY,
          name TEXT NOT NULL
        );
        CREATE TABLE media_items (
          id TEXT PRIMARY KEY,
          type TEXT NOT NULL,
          title TEXT NOT NULL,
          normalized_title TEXT NOT NULL,
          year INTEGER NULL,
          show_title TEXT NULL,
          season_number INTEGER NULL,
          episode_number INTEGER NULL,
          created_at TEXT NOT NULL
        );
        CREATE TABLE media_versions (
          id TEXT PRIMARY KEY,
          media_id TEXT NOT NULL REFERENCES media_items(id)
        );
        CREATE TABLE sources (
          id TEXT PRIMARY KEY,
          version_id TEXT NOT NULL REFERENCES media_versions(id),
          kind TEXT NOT NULL,
          locator_protected BLOB NOT NULL,
          locator_redacted TEXT NOT NULL,
          fingerprint TEXT NOT NULL,
          may_expire INTEGER NOT NULL,
          is_loopback INTEGER NOT NULL,
          stremio_hint INTEGER NOT NULL,
          seekable INTEGER NULL,
          dependency_note TEXT NULL,
          preferred INTEGER NOT NULL
        );
        CREATE UNIQUE INDEX ux_sources_fingerprint ON sources(fingerprint);
        CREATE TABLE subtitles (
          id TEXT PRIMARY KEY,
          version_id TEXT NOT NULL REFERENCES media_versions(id),
          locator_protected BLOB NOT NULL,
          locator_redacted TEXT NOT NULL
        );
        CREATE TABLE playback_state (
          profile_id TEXT NOT NULL,
          media_id TEXT NOT NULL,
          version_id TEXT NOT NULL,
          position_ms INTEGER NOT NULL,
          duration_ms INTEGER NOT NULL,
          completed INTEGER NOT NULL,
          updated_at TEXT NOT NULL,
          last_source_id TEXT NOT NULL,
          PRIMARY KEY (profile_id, media_id)
        );
        CREATE TABLE artwork (
          media_id TEXT PRIMARY KEY REFERENCES media_items(id),
          role TEXT NOT NULL,
          cache_path TEXT NOT NULL,
          manual_override INTEGER NOT NULL
        );
        CREATE TABLE settings (
          key TEXT PRIMARY KEY,
          value TEXT NOT NULL
        );
        """;
}
```

The protected blob is the only stored form of the original URL. `locator_redacted` is the clear-text column. `UpdateLocator` does not write `playback_state`.

- [ ] **Step 5: Run the tests and confirm they pass**

```powershell
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj
```

Expected: PASS. `supersecretvalue` is absent from the database file. The round-trip locator still matches the original URL.

- [ ] **Step 6: Commit when commits are requested**

```powershell
git add src/Application/LibraryContracts.cs src/Persistence tests/Persistence.Tests
git commit -m "feat: store titles and progress without writing tokens in clear text"
```

## Task 6: Import, replace, and the playback coordinator

**Files:**
- Create: `src/Application/LibraryController.cs`
- Create: `src/Application/PlaybackCoordinator.cs`
- Create: `tests/Persistence.Tests/LibraryControllerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `tests/Persistence.Tests/LibraryControllerTests.cs`:

```csharp
using Defuse.Application;
using Defuse.Domain;
using Defuse.Persistence;

namespace Defuse.Persistence.Tests;

public sealed class LibraryControllerTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "defuse-tests", Guid.NewGuid().ToString("N"));
    private readonly SqliteLibraryStore _store;
    private readonly LibraryController _library;

    public LibraryControllerTests()
    {
        Directory.CreateDirectory(_directory);
        _store = new SqliteLibraryStore(Path.Combine(_directory, "library.db"), new XorSecretProtector());
        _store.Initialize();
        _library = new LibraryController(_store, Path.Combine(_directory, "artwork"));
    }

    [Fact]
    public void Same_path_with_a_new_token_updates_the_existing_title()
    {
        var first = _library.Save(Request("https://cdn.example/a.mkv?token=one", "Night"));
        var second = _library.Save(Request("https://cdn.example/a.mkv?token=two", "Night"));
        Assert.Equal(first.MediaId, second.MediaId);
        Assert.True(second.UpdatedExisting);
        Assert.Single(_library.Library());
    }

    [Fact]
    public void Manifest_is_refused_and_the_library_stays_empty()
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            _library.Save(Request("https://addon.example/manifest.json", "Addon")));
        Assert.Contains("addon", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_library.Library());
    }

    [Fact]
    public void Replace_keeps_the_saved_position()
    {
        var saved = _library.Save(Request("https://cdn.example/a.mkv?token=old", "Night"));
        var target = _store.GetPlayTarget(saved.MediaId)!;
        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, target.MediaId, target.VersionId, 2_530_000, 7_200_000, false,
            DateTimeOffset.UnixEpoch, target.SourceId));
        var replaced = _library.Replace(saved.MediaId, "https://cdn.example/b.mkv?token=new");
        Assert.Equal(saved.MediaId, replaced.MediaId);
        Assert.Equal(2_530_000, _store.GetProgress(LibraryProfile.Id, saved.MediaId)!.PositionMs);
        Assert.Equal("https://cdn.example/b.mkv?token=new", replaced.Locator);
    }

    [Fact]
    public void Transient_play_does_not_insert_a_row()
    {
        var target = _library.CreateTransient("https://cdn.example/a.mp4", null, false);
        Assert.Equal(Guid.Empty, target.MediaId);
        Assert.Empty(_library.Library());
    }

    [Fact]
    public void Play_records_the_resume_point_before_the_file_opens()
    {
        var saved = _library.Save(Request("https://cdn.example/a.mp4", "Night"));
        var target = _store.GetPlayTarget(saved.MediaId)!;
        _store.UpsertProgress(new PlaybackProgress(
            LibraryProfile.Id, target.MediaId, target.VersionId, 2_530_000, 7_200_000, false,
            DateTimeOffset.UnixEpoch, target.SourceId));
        target = _store.GetPlayTarget(saved.MediaId)!;
        var engine = new FakeEngine();
        var coordinator = new PlaybackCoordinator(engine, _store);
        coordinator.Play(target, 2_530_000, persist: true);
        Assert.Equal(2_530_000, _store.GetProgress(LibraryProfile.Id, saved.MediaId)!.PositionMs);
        Assert.Equal(new Uri(target.Locator), engine.Loaded);
        coordinator.StopPersisting();
        engine.Current = new PlayerSnapshot(0, 7_200_000, true, false, true);
        coordinator.Flush();
        Assert.Equal(2_530_000, _store.GetProgress(LibraryProfile.Id, saved.MediaId)!.PositionMs);
    }

    public void Dispose() => _store.Dispose();

    private static SaveRequest Request(string url, string title) =>
        new(url, title, "video", null, null, null, null, null, null, null, false);

    private sealed class FakeEngine : IPlayerEngine
    {
        public PlayerSnapshot Current { get; set; }
        public Uri? Loaded { get; private set; }
        public event EventHandler<PlayerSnapshot>? SnapshotChanged;
        public event EventHandler? Ended;
        public event EventHandler<PlayerFault>? Faulted;
        public void Load(Uri locator, long startPositionMs, string? subtitleLocator) => Loaded = locator;
        public void Pause() { }
        public void ResumePlayback() { }
        public void Seek(long positionMs) { }
        public void Stop() { }
        public void SetVolume(int volume0To100) { }
        public void Dispose() { }
    }
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

```powershell
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj --filter LibraryControllerTests
```

Expected: FAIL because `LibraryController` does not exist.

- [ ] **Step 3: Implement import and the coordinator**

Create `src/Application/LibraryController.cs`:

```csharp
using Defuse.Domain;
using Defuse.Sources.Direct;

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

    public PlayTarget Replace(Guid mediaId, string rawUrl)
    {
        var current = _store.GetPlayTarget(mediaId)
            ?? throw new InvalidOperationException("That title is no longer in the library.");
        var before = _store.GetProgress(LibraryProfile.Id, mediaId);
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
            TitleNormalizer.Normalize(current.Title), null, null, null, null, link.Class.ToString(),
            locator, link.RedactedDisplay, fingerprint, link.MayExpire, link.IsLoopback,
            link.SuggestsStremioServer, link.UserMessage, subtitle.Item1, subtitle.Item2));
        var after = _store.GetProgress(LibraryProfile.Id, mediaId);
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
            null, link.IsLoopback, link.MayExpire, link.SuggestsStremioServer, link.RedactedDisplay, link.UserMessage, null);
    }

    public PlayTarget? Open(Guid mediaId) => _store.GetPlayTarget(mediaId);
    public IReadOnlyList<ContinueItem> Continue() => _store.ListContinueWatching(LibraryProfile.Id);
    public IReadOnlyList<LibraryItem> Library() => _store.ListLibrary();
    public void MarkUnwatched(Guid mediaId) => _store.ClearProgress(LibraryProfile.Id, mediaId);
    public bool ClipboardWatch() => _store.GetClipboardWatch();
    public void SetClipboardWatch(bool enabled) => _store.SetClipboardWatch(enabled);

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
        var raw = link.LocalPath is not null
            ? Path.GetFileNameWithoutExtension(link.LocalPath)
            : link.Uri is null ? "" : Uri.UnescapeDataString(Path.GetFileNameWithoutExtension(link.Uri.AbsolutePath));
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
            locator, link.RedactedDisplay, fingerprint, link.MayExpire, link.IsLoopback, link.SuggestsStremioServer,
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
```

Create `src/Application/PlaybackCoordinator.cs`:

```csharp
using Defuse.Domain;

namespace Defuse.Application;

public sealed class PlaybackCoordinator
{
    private readonly IPlayerEngine _engine;
    private readonly ILibraryStore _store;
    private readonly ProgressTracker _tracker = new();
    private PlayTarget? _current;
    private bool _persist;
    private bool _seekableNoted;

    public PlaybackCoordinator(IPlayerEngine engine, ILibraryStore store)
    {
        _engine = engine;
        _store = store;
    }

    public ResumeDecision Decide(PlayTarget target)
    {
        var duration = target.Progress?.DurationMs ?? 0;
        var seekable = target.SeekableKnown ?? duration > 0;
        var live = target.SeekableKnown == false && duration <= 0;
        return ResumePolicy.Evaluate(target.Progress, new PlaybackCapabilities(seekable, live));
    }

    public void Play(PlayTarget target, long startMs, bool persist)
    {
        _current = target;
        _persist = persist;
        _seekableNoted = target.SeekableKnown is not null;
        if (persist)
        {
            var row = _tracker.Start(
                LibraryProfile.Id, target.MediaId, target.VersionId, target.SourceId, startMs,
                target.Progress?.DurationMs ?? 0, DateTimeOffset.UtcNow);
            _store.UpsertProgress(row);
        }

        _engine.Load(new Uri(target.Locator), startMs, target.SubtitleLocator);
    }

    public void OnSnapshot(PlayerSnapshot snapshot)
    {
        Note(snapshot.PositionMs, snapshot.DurationMs, force: false);
        NoteSeekable(snapshot);
    }

    public void Pause()
    {
        _engine.Pause();
        Flush();
    }

    public void ResumePlayback() => _engine.ResumePlayback();

    public void Seek(long positionMs, long durationMs)
    {
        _engine.Seek(positionMs);
        Note(positionMs, durationMs, force: true);
    }

    public void Flush()
    {
        var snapshot = _engine.Current;
        Note(snapshot.PositionMs, snapshot.DurationMs, force: true);
    }

    public void StopPersisting() => _persist = false;

    public void Stop()
    {
        Flush();
        _persist = false;
        _current = null;
        _engine.Stop();
    }

    private void Note(long positionMs, long durationMs, bool force)
    {
        if (!_persist || _current is null)
            return;
        var now = DateTimeOffset.UtcNow;
        var row = force
            ? _tracker.Flush(positionMs, durationMs, now)
            : _tracker.Observe(positionMs, durationMs, now);
        if (row is not null)
            _store.UpsertProgress(row);
    }

    private void NoteSeekable(PlayerSnapshot snapshot)
    {
        if (_current is null || _seekableNoted || _current.SourceId == Guid.Empty)
            return;
        if (snapshot.DurationMs > 0)
        {
            _store.SetSeekable(_current.SourceId, snapshot.IsSeekable);
            _seekableNoted = true;
        }
        else if (snapshot.PlaybackStarted && !snapshot.IsSeekable)
        {
            _store.SetSeekable(_current.SourceId, false);
            _seekableNoted = true;
        }
    }
}
```

Only this class calls `Load`, `Pause`, `ResumePlayback`, `Seek`, and `Stop`.

- [ ] **Step 4: Run the tests and confirm they pass**

```powershell
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj
dotnet test tests/Domain.Tests/Defuse.Domain.Tests.csproj
dotnet test tests/Adapter.Tests/Defuse.Adapter.Tests.csproj
```

Expected: PASS.

- [ ] **Step 5: Commit when commits are requested**

```powershell
git add src/Application tests/Persistence.Tests/LibraryControllerTests.cs
git commit -m "feat: save a link without tying progress to the token"
```

## Task 7: Library shell

**Files:**
- Create: `src/Desktop/Theme.xaml`
- Create: `src/Desktop/PositionLabel.cs`
- Create: `src/Desktop/MainWindow.xaml`
- Create: `src/Desktop/MainWindow.xaml.cs`
- Create: `src/Desktop/AddLinkWindow.xaml`
- Create: `src/Desktop/AddLinkWindow.xaml.cs`
- Create: `src/Desktop/ReplaceLinkWindow.xaml`
- Create: `src/Desktop/ReplaceLinkWindow.xaml.cs`
- Modify: `src/Desktop/App.xaml`
- Modify: `src/Desktop/App.xaml.cs`
- Delete: `src/Desktop/SpikeWindow.xaml`
- Delete: `src/Desktop/SpikeWindow.xaml.cs`

This task replaces the spike window. The engine from Task 2 stays.

- [ ] **Step 1: Add the theme and position label**

Create `src/Desktop/Theme.xaml`:

```xml
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <SolidColorBrush x:Key="Bg" Color="#121418"/>
    <SolidColorBrush x:Key="Surface" Color="#1C1F27"/>
    <SolidColorBrush x:Key="Text" Color="#F4F1EA"/>
    <SolidColorBrush x:Key="Muted" Color="#A7A39A"/>
    <SolidColorBrush x:Key="Accent" Color="#D7A15F"/>
    <Style TargetType="Window">
        <Setter Property="Background" Value="{StaticResource Bg}"/>
        <Setter Property="Foreground" Value="{StaticResource Text}"/>
        <Setter Property="FontFamily" Value="Segoe UI Variable, Segoe UI"/>
        <Setter Property="FontSize" Value="14"/>
    </Style>
    <Style TargetType="Button">
        <Setter Property="Background" Value="{StaticResource Surface}"/>
        <Setter Property="Foreground" Value="{StaticResource Text}"/>
        <Setter Property="Padding" Value="12,8"/>
        <Setter Property="BorderBrush" Value="{StaticResource Accent}"/>
        <Setter Property="BorderThickness" Value="1"/>
    </Style>
    <Style TargetType="TextBox">
        <Setter Property="Background" Value="{StaticResource Surface}"/>
        <Setter Property="Foreground" Value="{StaticResource Text}"/>
        <Setter Property="CaretBrush" Value="{StaticResource Text}"/>
        <Setter Property="Padding" Value="8,6"/>
    </Style>
    <Style TargetType="ListBox">
        <Setter Property="Background" Value="Transparent"/>
        <Setter Property="BorderThickness" Value="0"/>
        <Setter Property="Foreground" Value="{StaticResource Text}"/>
    </Style>
    <Style TargetType="ListBoxItem">
        <Setter Property="Padding" Value="8"/>
        <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        <Style.Triggers>
            <Trigger Property="IsKeyboardFocused" Value="True">
                <Setter Property="Background" Value="#2A3140"/>
            </Trigger>
        </Style.Triggers>
    </Style>
</ResourceDictionary>
```

Create `src/Desktop/PositionLabel.cs` implementing `IValueConverter`. `Convert` accepts a `long` millisecond value and returns `h:mm:ss` when the span is at least one hour, otherwise `m:ss`. Null and non-positive values return `""`. `ConvertBack` throws `NotSupportedException`.

Replace `src/Desktop/App.xaml` so it merges `Theme.xaml` and has no `StartupUri`.

- [ ] **Step 2: Add the add and replace windows**

`AddLinkWindow` constructor takes `LibraryController` and an optional initial URL. On URL text change, call `Preview` and show `Classification.UserMessage`. When the title box has not been edited, fill it with `SuggestedTitle`. Show a combo of `Save as a new title` plus each `TitleMatch`. The default is a new title. Fields: title, type (`video`, `movie`, `episode`, `external`), year, show, season, episode, subtitle URL, poster path, and a checkbox `Try to play this even though the link has no video extension` visible only for `UnknownHttp`. A checkbox `When I copy a video link, offer to add it` reads and writes `ClipboardWatch`. Buttons:

- `Save & Play` calls `Save` with `PlayUnknownAsVideo` from the checkbox, sets `SavedMediaId` and `PlayAfterSave = true`, and sets `DialogResult`.
- `Play without saving` calls `CreateTransient` and sets `Transient`.
- `Save shortcut` is enabled for `ExternalPage` and for `UnknownHttp` when the try-to-play box is unchecked. It calls `Save` with `PlayAfterSave = false`.

Catch `InvalidOperationException` and put `ex.Message` in the message text. Do not show the raw URL in that text; the controller messages do not include it.

`ReplaceLinkWindow` takes the controller and a media id. Its button calls `Replace` and stores the `PlayTarget` in `Replaced`.

- [ ] **Step 3: Add the main window**

`MainWindow` takes `LibraryController`, `PlaybackCoordinator`, and `LibVlcEngine`.

Layout:

- `LibraryLayer` is Home: wordmark `Defuse`, `Add link`, `Search`, a Continue list, and a Library list. Empty state: `Add a video link to start your library.` Lists are virtualizing `ListBox` controls. A row shows the poster when `PosterPath` is set, the title, and `RedactedLocator`. Continue rows also show the position through `PositionLabel`.
- `Search` opens an overlay on Home, Details, or the Show page. It filters saved titles as the user types. Escape closes it. It does not search people or genres.
- Selecting a title opens `Details`: poster, title, redacted source, Resume or Play, and `Sources`. Back returns Home. A saved episode also shows its show name, season, and episode. The Show page lists saved episodes that share `show_title`, with a watched mark when progress is complete. Play Next is not in this release.
- A clipboard banner, collapsed until needed: `Add the copied video link?` with `Add` and `Not now`.
- `PlayerLayer` is its own view. The video fills the window. Move the overlay grid into the `VideoView` when playback starts, and move it back out before leaving the view. Controls fade out after 3 seconds and return on mouse movement or a key. Reduced motion leaves them visible.
  - Top left: `Back` to Details, the movie title, and the episode name when the item has one.
  - Center: a brief play/pause mark when playback toggles. No Skip Intro or Next Episode button.
  - Bottom: play/pause, current time, seek bar, remaining time, volume, fullscreen.
  - Bottom right: `Sources` and `More`.
  - `Sources` is a panel over the player. It lists the current redacted link and offers `Replace link`. Choosing a replacement continues at the current timestamp.
  - `More` contains `Start over` and the current position, duration, and whether the stream is seekable. Subtitle, audio, speed, aspect, chapters, and picture in picture are not controls in this release.
  - On failure the video surface stays. Show Retry, Choose Another Source, and Replace Link, plus `PlaybackFailureCopy`. Do not return to Home automatically.

Behavior:

- `Add link` and a text drop open `AddLinkWindow`. A saved id opens that title. `PlayAfterSave` plays it. `Transient` plays with `persist: false`.
- Details `Play` calls `Open` and `Decide`. `FromStart` plays at 0. `ResumeQuiet` plays at `PositionMs`. `OfferResume` shows `Resume` at the formatted time and `Start over` on Details before the video starts. `Completed` shows `Start over` and `Resume near the end`. `Unseekable` shows `Explanation` and `Play`, which starts at 0.
- `Resume` plays at the offered position with `persist: true`. `Start over` plays at 0 and the coordinator's `Play` overwrites the stored position. The same action lives in the player's More panel.
- Snapshot events update the slider when the user is not dragging. Show remaining time as duration minus position when duration is greater than 0. Releasing the slider calls `Seek`. `Pause` calls `coordinator.Pause` and flushes. Space does the same when a text box is not focused, and flashes the center play/pause mark. Left and Right seek by 10 seconds. Up and Down change volume by 5 within 0–100. F toggles borderless maximized fullscreen. Escape leaves fullscreen first. A second Escape returns to Details and flushes.
- `Ended` flushes at the duration so the completion rule can mark it finished, then returns to Details.
- `Faulted` calls `StopPersisting`, keeps the player view up, and shows `PlaybackFailureCopy.Describe` using the target flags. It does not flush a reset position. `Retry` plays the same target at the last saved position. `Choose Another Source` opens the Sources panel. `Replace link` opens `ReplaceLinkWindow` and, on success, plays that target at the saved position.
- Closing the window calls `coordinator.Stop()`, which flushes, then disposes the engine from `App.OnExit` only after the window flush. `MainWindow.OnClosing` calls `Stop` first.
- `Activated` reads the clipboard only when `ClipboardWatch()` is true. Ignore clipboard exceptions. Offer the banner when the text classifies as playable or `UnknownHttp`, and only when the text changed.
- `Mark unwatched` calls `MarkUnwatched`. `Remove` asks with `MessageBox` `Yes/No` and then `Delete`.
- Refresh both lists after every save, delete, mark, and return from the player. Hide the empty state when the library has items. Hide the Continue heading when that list is empty.
- Do not put the `VideoView` inside a `ScrollViewer`. Do not use a `Popup` over the video.
- When animations are disabled (`SystemParameters.ClientAreaAnimation` or `MenuAnimation` is false), leave the overlay visible. Otherwise hide the overlay 3 seconds after the pointer stops moving over the video, and show it again on pointer movement.

Replace `App.xaml.cs` so `OnStartup` creates `SqliteLibraryStore` at `AppPaths.Database` with `DpapiSecretProtector`, calls `Initialize`, creates `LibVlcEngine` with `WpfUiMarshal`, creates the controller with `AppPaths.Artwork`, creates the coordinator, and shows `MainWindow`. A failed `Initialize` shows `LogScrubber.Scrub` of the exception message and shuts down. `DispatcherUnhandledException` does the same and sets `Handled`.

Create `src/Desktop/AppPaths.cs`:

```csharp
namespace Defuse.Desktop;

public static class AppPaths
{
    public static string Root =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Defuse");

    public static string Database => Path.Combine(Root, "library.db");
    public static string Artwork => Path.Combine(Root, "artwork");
}
```

Delete the spike window files.

- [ ] **Step 4: Build**

```powershell
dotnet build src/Desktop/Defuse.Desktop.csproj
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj
```

Expected: build succeeds and the persistence tests still pass. Run the app and confirm Home appears before any network request. The empty state is the library sentence, not a spinner.

- [ ] **Step 5: Commit when commits are requested**

```powershell
git add src/Desktop
git commit -m "feat: add the library shell around in-app playback"
```

## Task 8: Accept the paste, resume, and replace journey

**Files:**
- Modify: `docs/sample-matrix.md`
- Modify: `docs/feature-parity.md`

- [ ] **Step 1: Run the automated tests**

```powershell
dotnet test tests/Domain.Tests/Defuse.Domain.Tests.csproj
dotnet test tests/Adapter.Tests/Defuse.Adapter.Tests.csproj
dotnet test tests/Persistence.Tests/Defuse.Persistence.Tests.csproj
```

Expected: PASS.

- [ ] **Step 2: Walk the journey on this PC**

```powershell
dotnet run --project src/Desktop/Defuse.Desktop.csproj
```

1. Add `https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerEscapes.mp4`. Set the title to `Escape`. Save & Play. Confirm the picture is inside the window and the overlay controls work.
2. Seek to about 00:45 if the file is long enough, or play past 30 seconds. Close the window.
3. Open the app. `Escape` is under Continue. Resume. Playback starts within a few seconds of the quit position. Record the two timestamps in `docs/sample-matrix.md`.
4. Paste `https://addon.example/manifest.json`. The message says it is an addon link. The library still contains `Escape`.
5. Paste `stremio://detail/movie/tt1`. The message says it is not a video file.
6. Drop a text URL onto the window. The add dialog opens with that text.
7. Enable the clipboard offer, copy an `https://` video URL, and focus the window. The banner appears. With the offer off, focusing the window does not read a new banner into view.
8. Replace the sample URL with `https://commondatastorage.googleapis.com/gtv-videos-bucket/sample/ForBiggerBlazes.mp4`. Resume still offers the previous position. The title is still `Escape`.
9. Kill the process from Task Manager during playback. Reopen. The position is within about 5 seconds of the last moment you saw. Record that in the matrix.
10. Search `%LOCALAPPDATA%\Defuse\library.db` and the newest log-free UI for a token you put only in a query string, such as `supersecretvalue`. It must not appear. The redacted row may show the host and path.

Write pass or fail in `docs/sample-matrix.md`. In `docs/feature-parity.md`, change a P0 row to `Done` only for a step you actually saw pass. Leave the user's own copied link blank until they provide a redacted sample and you play it.

- [ ] **Step 3: Commit when commits are requested**

```powershell
git add docs/sample-matrix.md docs/feature-parity.md
git commit -m "docs: record the first resume and replace results"
```

## Spec coverage

| Contract item | Task |
| --- | --- |
| Paste, classify, preview, save, play | 3, 6, 7 |
| Player fills the window; controls fade and return | 7 |
| Failure stays on the video with retry, another source, and replace | 7 |
| Source change keeps the timestamp | 6, 7 |
| Details, show list for saved episodes, and title search overlay | 7 |
| Play without saving | 6, 7 |
| Clipboard consent and drag/drop | 7 |
| Manual title, type, season, episode, poster, subtitle URL | 6, 7 |
| Progress every 5 seconds, on start, seek, pause, and close | 4, 6, 7 |
| Quiet resume under 30 seconds, prompt through 95 percent | 4, 7 |
| Continue Watching | 5, 7 |
| Replace link keeps media id and position | 5, 6, 7 |
| Source failure leaves the item in the library | 6, 7 |
| Tokens redacted and protected | 3, 5 |
| Manifest, deep link, magnet, folder, UNC, playlist refused | 3, 6 |
| Single local file | 3, 6 |
| Four-sample playback spike before the shell | 2 |
| Schema preserved across a second open | 5 |
| 1,000 titles list from SQLite | 5 |

Phases 3–10 in `docs/master-plan.md` have no tasks in this plan.
