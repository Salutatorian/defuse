using System.Globalization;
using Defuse.Application;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using VlcLibrary = LibVLCSharp.Shared.LibVLC;

namespace Defuse.Playback.Cross;

public sealed class LibVlcEngine : IPlayerEngine
{
    private static readonly object Gate = new();
    private static bool _ready;
    private readonly IUiMarshal _ui;
    private readonly VlcLibrary _libVlc;
    private readonly object _clock = new();
    private bool _disposed;
    private long _time;
    private long _length;
    private bool _seekable;
    private bool _playing;
    private long _heldTime = -1;
    private long _holdUntil;

    public LibVlcEngine(IUiMarshal ui, string? nativeDirectory = null)
    {
        _ui = ui;
        lock (Gate)
        {
            if (!_ready)
            {
                PrepareNative(nativeDirectory);
                _ready = true;
            }
        }

        var options = new List<string>
        {
            "--network-caching=5000",
            "--file-caching=300",
            "--live-caching=300",
            "--disc-caching=300",
            "--avcodec-threads=6",
            "--no-drop-late-frames",
            "--no-skip-frames",
            "--no-avcodec-hurry-up",
            "--no-keyboard-events",
            "--no-mouse-events"
        };
        if (OperatingSystem.IsWindows())
            options.Add("--d3d11-hdr-mode=never");
        _libVlc = new VlcLibrary(options.ToArray());
        Player = new MediaPlayer(_libVlc);
        Player.Playing += OnPlaying;
        Player.TimeChanged += OnTimeChanged;
        Player.LengthChanged += OnLengthChanged;
        Player.EndReached += OnEndReached;
        Player.EncounteredError += OnEncounteredError;
        Player.Paused += OnPaused;
        Player.Buffering += OnBuffering;
    }

    public MediaPlayer Player { get; }

    public PlayerSnapshot Current => new(
        Math.Max(0, Player.Time),
        Math.Max(0, Player.Length),
        Player.IsSeekable,
        !Player.IsPlaying,
        Player.IsPlaying || Player.Time > 0,
        null);

    public float Rate => Player.Rate;
    public IReadOnlyList<TrackChoice> AudioTracks => Describe(Player.AudioTrackDescription);
    public IReadOnlyList<TrackChoice> SubtitleTracks => Describe(Player.SpuDescription);
    public IReadOnlyList<ChapterMark> Chapters => ReadChapters();
    public PlaybackInfo Info => ReadInfo();

    public event EventHandler<PlayerSnapshot>? SnapshotChanged;
    public event EventHandler? Ended;
    public event EventHandler<PlayerFault>? Faulted;
    public event EventHandler<bool>? LoadingChanged;

    public static string? FindNativeDirectory()
    {
        var beside = Path.Combine(AppContext.BaseDirectory, "lib");
        if (HasNativeLibrary(beside))
            return beside;
        if (OperatingSystem.IsMacOS())
        {
            const string installed = "/Applications/VLC.app/Contents/MacOS/lib";
            if (HasNativeLibrary(installed))
                return installed;
        }

        if (!OperatingSystem.IsWindows() && HasNativeLibrary(AppContext.BaseDirectory))
            return AppContext.BaseDirectory;
        return FindLinuxLibVlc();
    }

    public static string? FindLinuxLibVlc()
    {
        if (!OperatingSystem.IsLinux())
            return null;
        foreach (var dir in new[]
        {
            "/usr/lib/x86_64-linux-gnu",
            "/usr/lib/aarch64-linux-gnu",
            "/usr/lib64",
            "/usr/lib",
            "/usr/local/lib"
        })
        {
            if (File.Exists(Path.Combine(dir, "libvlc.so.5")) || File.Exists(Path.Combine(dir, "libvlc.so")))
                return dir;
        }

        return null;
    }

    private static void PrepareNative(string? nativeDirectory)
    {
        if (OperatingSystem.IsLinux())
        {
            var directory = string.IsNullOrWhiteSpace(nativeDirectory) ? FindLinuxLibVlc() : nativeDirectory;
            if (!string.IsNullOrWhiteSpace(directory))
            {
                PrependPathVariable("LD_LIBRARY_PATH", directory);
                var plugins = Path.Combine(directory, "vlc", "plugins");
                if (!Directory.Exists(plugins))
                    plugins = Path.GetFullPath(Path.Combine(directory, "..", "plugins"));
                if (Directory.Exists(plugins))
                    Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
            }

            Core.Initialize();
            return;
        }

        if (!string.IsNullOrWhiteSpace(nativeDirectory))
        {
            var plugins = Path.GetFullPath(Path.Combine(nativeDirectory, "..", "plugins"));
            if (Directory.Exists(plugins))
                Environment.SetEnvironmentVariable("VLC_PLUGIN_PATH", plugins);
            Core.Initialize(nativeDirectory);
            return;
        }

        Core.Initialize();
    }

    private static void PrependPathVariable(string name, string directory)
    {
        var current = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(current))
            Environment.SetEnvironmentVariable(name, directory);
        else if (!current.Split(Path.PathSeparator).Contains(directory, StringComparer.Ordinal))
            Environment.SetEnvironmentVariable(name, directory + Path.PathSeparator + current);
    }

    private static bool HasNativeLibrary(string directory)
    {
        if (!Directory.Exists(directory))
            return false;
        return File.Exists(Path.Combine(directory, "libvlc.dylib"))
            || File.Exists(Path.Combine(directory, "libvlc.so"))
            || File.Exists(Path.Combine(directory, "libvlc.so.5"));
    }

    public void Load(Uri locator, long startPositionMs, string? subtitleLocator)
    {
        using var media = new Media(_libVlc, locator);
        if (startPositionMs > 0)
            media.AddOption(":start-time=" + (startPositionMs / 1000d).ToString(CultureInfo.InvariantCulture));
        media.AddOption(":network-caching=5000");
        media.AddOption(":file-caching=300");
        media.AddOption(":live-caching=300");
        if (!string.IsNullOrWhiteSpace(subtitleLocator))
            media.AddSlave(MediaSlaveType.Subtitle, 4, subtitleLocator);
        Player.Play(media);
    }

    public void Pause() => Player.SetPause(true);
    public void ResumePlayback() => Player.SetPause(false);

    public void Seek(long positionMs)
    {
        if (_disposed)
            return;
        var time = Math.Max(0, positionMs);
        lock (_clock)
        {
            _time = time;
            _heldTime = time;
            _holdUntil = Environment.TickCount64 + 1600;
        }

        try
        {
            Player.Time = time;
        }
        catch (Exception)
        {
        }
    }

    public void Cover(int hostWidth, int hostHeight) => Fit();
    public void Fit()
    {
        if (!_disposed)
            Player.Scale = 0;
    }

    public void Stop() => Player.Stop();
    public void SetVolume(int volume0To100) => Player.Volume = Math.Clamp(volume0To100, 0, 100);
    public void SetRate(float rate) => Player.SetRate(rate);
    public void SelectAudio(int id) => Player.SetAudioTrack(id);
    public void SelectSubtitle(int id) => Player.SetSpu(id);
    public void SetSubtitleDelay(long delayMs) => Player.SetSpuDelay(delayMs * 1000);
    public void SetChapter(int index) => Player.Chapter = index;

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Player.Playing -= OnPlaying;
        Player.TimeChanged -= OnTimeChanged;
        Player.LengthChanged -= OnLengthChanged;
        Player.EndReached -= OnEndReached;
        Player.EncounteredError -= OnEncounteredError;
        Player.Paused -= OnPaused;
        Player.Buffering -= OnBuffering;
        Player.Dispose();
        _libVlc.Dispose();
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        _playing = true;
        Publish(_time, paused: false);
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        var reported = Math.Max(0, e.Time);
        long time;
        lock (_clock)
        {
            if (_heldTime >= 0 && Environment.TickCount64 < _holdUntil && Math.Abs(reported - _heldTime) > 4000)
                return;
            if (Environment.TickCount64 >= _holdUntil)
                _heldTime = -1;
            _time = reported;
            time = _time;
        }

        Publish(time, paused: !_playing);
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        _length = Math.Max(0, e.Length);
        _seekable = _length > 0;
        Publish(_time, paused: !_playing);
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        _playing = false;
        Publish(_time, paused: true);
    }

    private void OnBuffering(object? sender, MediaPlayerBufferingEventArgs e)
    {
        if (_disposed)
            return;
        var loading = e.Cache < 99.5f;
        _ui.Post(() => LoadingChanged?.Invoke(this, loading));
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        _ui.Post(() => Ended?.Invoke(this, EventArgs.Empty));
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        if (_disposed)
            return;
        _ui.Post(() => Faulted?.Invoke(this, new PlayerFault(false, "Playback failed.")));
    }

    private void Publish(long time, bool paused)
    {
        if (_disposed)
            return;
        var snapshot = new PlayerSnapshot(Math.Max(0, time), _length, _seekable, paused, _playing || time > 0, null);
        _ui.Post(() => SnapshotChanged?.Invoke(this, snapshot));
    }

    private static IReadOnlyList<TrackChoice> Describe(IEnumerable<TrackDescription>? tracks)
    {
        if (tracks is null)
            return [];
        return tracks.Select(track => new TrackChoice(track.Id, string.IsNullOrWhiteSpace(track.Name) ? "Track" : track.Name)).ToArray();
    }

    private IReadOnlyList<ChapterMark> ReadChapters()
    {
        var chapters = Player.FullChapterDescriptions(-1);
        if (chapters is null || chapters.Length == 0)
            return [];
        var list = new List<ChapterMark>();
        for (var i = 0; i < chapters.Length; i++)
            list.Add(new ChapterMark(i, string.IsNullOrWhiteSpace(chapters[i].Name) ? $"Chapter {i + 1}" : chapters[i].Name!, chapters[i].TimeOffset));
        return list;
    }

    private PlaybackInfo ReadInfo()
    {
        var video = "Video";
        var width = 0;
        var height = 0;
        var tracks = Player.Media?.Tracks;
        if (tracks is not null)
        {
            foreach (var track in tracks)
            {
                if (track.TrackType != TrackType.Video)
                    continue;
                var frame = track.Data.Video;
                width = (int)frame.Width;
                height = (int)frame.Height;
                if (width > 0)
                    video = $"{width}×{height}";
                break;
            }
        }

        return new PlaybackInfo(video, "Audio", width, height, Rate);
    }
}
