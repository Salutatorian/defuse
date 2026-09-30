using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using Defuse.Application;
using LibVLCSharp.Shared;
using LibVLCSharp.Shared.Structures;
using VlcLibrary = LibVLCSharp.Shared.LibVLC;

namespace Defuse.Playback.LibVLC;

public sealed class LibVlcEngine : IPlayerEngine
{
    private static readonly object Gate = new();
    private static bool _ready;
    private readonly IUiMarshal _ui;
    private readonly VlcLibrary _libVlc;
    private bool _disposed;
    private int _subtitleSize = 100;
    private int _subtitleColor = 0xFFFFFF;
    private bool _styleOnRenderer;
    private int _shownSize = -1;
    private int _shownColor = -1;
    private long _styleShownAt;
    private int _subtitleOpacity = 255;
    private readonly object _clock = new();
    private long _time;
    private long _length;
    private bool _seekable;
    private bool _playing;
    private long _heldTime = -1;
    private long _holdUntil;

    public LibVlcEngine(IUiMarshal ui)
    {
        _ui = ui;
        lock (Gate)
        {
            if (!_ready)
            {
                Core.Initialize();
                _ready = true;
            }
        }

        _libVlc = new VlcLibrary(
            "--network-caching=5000",
            "--file-caching=300",
            "--live-caching=300",
            "--disc-caching=300",
            "--avcodec-threads=6",
            "--d3d11-hdr-mode=never",
            "--no-drop-late-frames",
            "--no-skip-frames",
            "--no-avcodec-hurry-up",
            "--no-keyboard-events",
            "--no-mouse-events");
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

    public void Load(Uri locator, long startPositionMs, string? subtitleLocator)
    {
        using var media = new Media(_libVlc, locator);
        if (startPositionMs > 0)
            media.AddOption(":start-time=" + (startPositionMs / 1000d).ToString(CultureInfo.InvariantCulture));
        media.AddOption(":network-caching=5000");
        media.AddOption(":file-caching=300");
        media.AddOption(":live-caching=300");
        _styleOnRenderer = false;
        _shownSize = -1;
        _shownColor = -1;
        media.AddOption(":sub-margin=180");
        media.AddOption(":freetype-fontsize=0");
        media.AddOption(":freetype-rel-fontsize=16");
        media.AddOption(":freetype-color=" + _subtitleColor.ToString(CultureInfo.InvariantCulture));
        media.AddOption(":freetype-opacity=" + _subtitleOpacity.ToString(CultureInfo.InvariantCulture));
        media.AddOption(":freetype-background-opacity=0");
        media.AddOption(":freetype-outline-thickness=0");
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

    public void SetSubtitleStyle(int fontSize, int color, int textOpacity, int backgroundOpacity, int outline, bool immediate = true)
    {
        _ = backgroundOpacity;
        _ = outline;
        _subtitleSize = Math.Clamp(fontSize, 1, 2000);
        _subtitleColor = color & 0xFFFFFF;
        _subtitleOpacity = textOpacity;
        if (PushSubtitleStyle())
            _styleOnRenderer = true;
        ReplaySubtitle(immediate);
    }

    public bool RefreshSubtitleStyle()
    {
        if (_styleOnRenderer)
            return true;
        if (!PushSubtitleStyle())
            return false;
        _styleOnRenderer = true;
        ReplaySubtitle(true);
        return true;
    }

    private bool PushSubtitleStyle()
    {
        if (_disposed)
            return false;
        var root = Player.NativeReference;
        if (root == IntPtr.Zero)
            return false;
        try
        {
            WriteStyle(root);
            if (Player.VoutCount < 1)
                return false;
            return VisitStyle(root, 0);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private bool VisitStyle(IntPtr obj, int depth)
    {
        if (depth > 8 || !Readable(obj, 8))
            return false;
        var type = ObjectType(obj);
        var found = false;
        if (type is "spu text" or "subpicture" or "video output" or "input")
        {
            WriteStyle(obj);
            if (type == "spu text")
                found = true;
        }

        var list = ListChildren(obj);
        if (list == IntPtr.Zero)
            return found;
        try
        {
            if (!Readable(list, 16))
                return found;
            var count = Marshal.ReadInt32(list, 4);
            var values = Marshal.ReadIntPtr(list, 8);
            var shown = Math.Min(Math.Max(count, 0), 80);
            if (shown == 0 || !Readable(values, shown * 8))
                return found;
            for (var i = 0; i < shown; i++)
            {
                if (VisitStyle(Marshal.ReadIntPtr(values, i * 8), depth + 1))
                    found = true;
            }
        }
        finally
        {
            ListRelease(list);
        }

        return found;
    }

    private void WriteStyle(IntPtr obj)
    {
        VarCreate(obj, "sub-text-scale", 0x0030);
        VarSetChecked(obj, "sub-text-scale", 0x0030, new VlcValue { Int = _subtitleSize });
        VarCreate(obj, "freetype-color", 0x0030);
        VarSetChecked(obj, "freetype-color", 0x0030, new VlcValue { Int = _subtitleColor });
        VarCreate(obj, "freetype-background-opacity", 0x0030);
        VarSetChecked(obj, "freetype-background-opacity", 0x0030, new VlcValue { Int = 0 });
        VarCreate(obj, "freetype-outline-thickness", 0x0030);
        VarSetChecked(obj, "freetype-outline-thickness", 0x0030, new VlcValue { Int = 0 });
    }

    private void ReplaySubtitle(bool force)
    {
        if (_subtitleSize == _shownSize && _subtitleColor == _shownColor)
            return;
        var now = Environment.TickCount64;
        if (!force && now - _styleShownAt < 80)
            return;
        if (_disposed || Player.VoutCount < 1)
            return;
        var id = Player.Spu;
        if (id < 0)
            return;
        _styleShownAt = now;
        _shownSize = _subtitleSize;
        _shownColor = _subtitleColor;
        try
        {
            var delay = Player.SpuDelay;
            Player.SetSpu(-1);
            Player.SetSpu(id);
            if (delay != 0)
                Player.SetSpuDelay(delay);
        }
        catch (Exception)
        {
            _shownSize = -1;
            _shownColor = -1;
        }
    }

    public void Fit()
    {
        if (!_disposed)
            Player.Scale = 0;
    }

    public void Cover(int hostWidth, int hostHeight)
    {
        if (_disposed || hostWidth < 2 || hostHeight < 2)
            return;
        var tracks = Player.Media?.Tracks;
        if (tracks is null)
            return;
        var width = 0;
        var height = 0;
        foreach (var track in tracks)
        {
            if (track.TrackType != TrackType.Video || track.Data.Video.Width == 0 || track.Data.Video.Height == 0)
                continue;
            width = (int)track.Data.Video.Width;
            height = (int)track.Data.Video.Height;
            break;
        }

        if (width == 0 || height == 0)
            return;
        Player.Scale = Math.Max(hostWidth / (float)width, hostHeight / (float)height);
    }
    public void Stop()
    {
        _styleOnRenderer = false;
        _shownSize = -1;
        _shownColor = -1;
        Player.Stop();
    }
    public void SetVolume(int volume0To100) => Player.Volume = Math.Clamp(volume0To100, 0, 100);
    public void SetRate(float rate) => Player.SetRate(rate);
    public void SelectAudio(int id) => Player.SetAudioTrack(id);
    public void SelectSubtitle(int id) => Player.SetSpu(id);
    public void SetSubtitleDelay(long delayMs) => Player.SetSpuDelay(delayMs * 1000);

    public bool SetSubtitleMargin(int pixels)
    {
        if (_disposed)
            return false;
        try
        {
            if (Player.VoutCount < 1)
                return false;
            return ApplyMargin(Player.NativeReference, Math.Clamp(pixels, -800, 4000), 0);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool ApplyMargin(IntPtr obj, int pixels, int depth)
    {
        if (depth > 6 || !Readable(obj, 8))
            return false;
        var placed = false;
        if (ObjectType(obj) == "video output")
            placed = VarSetChecked(obj, "sub-margin", 0x0030, new VlcValue { Int = pixels }) == 0;

        var list = ListChildren(obj);
        if (list == IntPtr.Zero)
            return placed;
        try
        {
            if (!Readable(list, 16))
                return placed;
            // i_type at 0 is left uninitialized. The count is the next int.
            var count = Marshal.ReadInt32(list, 4);
            var values = Marshal.ReadIntPtr(list, 8);
            if (count <= 0 || count > 32 || !Readable(values, count * 8))
                return placed;
            for (var i = 0; i < count; i++)
            {
                var child = Marshal.ReadIntPtr(values, i * 8);
                if (ApplyMargin(child, pixels, depth + 1))
                    placed = true;
            }
        }
        finally
        {
            ListRelease(list);
        }

        return placed;
    }

    private static string? ObjectType(IntPtr obj)
    {
        var text = Marshal.ReadIntPtr(obj);
        if (!Readable(text, 16))
            return null;
        var bytes = new byte[32];
        var available = ReadableLength(text, bytes.Length);
        if (available < 4)
            return null;
        Marshal.Copy(text, bytes, 0, available);
        var end = Array.IndexOf(bytes, (byte)0, 0, available);
        if (end < 0)
            end = available;
        return Encoding.ASCII.GetString(bytes, 0, end);
    }

    private static bool Readable(IntPtr address, int bytes)
    {
        return address != IntPtr.Zero && ReadableLength(address, bytes) >= bytes;
    }

    private static int ReadableLength(IntPtr address, int bytes)
    {
        if (address == IntPtr.Zero || bytes <= 0)
            return 0;
        var size = Marshal.SizeOf<MemoryRegion>();
        if (VirtualQuery(address, out var region, (IntPtr)size) == IntPtr.Zero)
            return 0;
        if (region.State != 0x1000 || (region.Protect & 0x100) != 0)
            return 0;
        var protect = region.Protect & 0xFF;
        if (protect is not (0x02 or 0x04 or 0x08 or 0x20 or 0x40 or 0x80))
            return 0;
        var start = address.ToInt64();
        var regionStart = region.BaseAddress.ToInt64();
        var regionEnd = regionStart + region.RegionSize.ToInt64();
        if (start < regionStart || start >= regionEnd)
            return 0;
        var room = (int)Math.Min(bytes, regionEnd - start);
        return room < 0 ? 0 : room;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryRegion
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public ushort PartitionId;
        public ushort Pad;
        public IntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct VlcValue
    {
        [FieldOffset(0)] public long Int;
    }

    [DllImport("libvlccore", CallingConvention = CallingConvention.Cdecl, EntryPoint = "var_Create")]
    private static extern int VarCreate(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int flags);

    [DllImport("libvlccore", CallingConvention = CallingConvention.Cdecl, EntryPoint = "var_SetChecked")]
    private static extern int VarSetChecked(IntPtr obj, [MarshalAs(UnmanagedType.LPUTF8Str)] string name, int type, VlcValue value);

    [DllImport("libvlccore", CallingConvention = CallingConvention.Cdecl, EntryPoint = "vlc_list_children")]
    private static extern IntPtr ListChildren(IntPtr obj);

    [DllImport("libvlccore", CallingConvention = CallingConvention.Cdecl, EntryPoint = "vlc_list_release")]
    private static extern void ListRelease(IntPtr list);

    [DllImport("kernel32", SetLastError = true)]
    private static extern IntPtr VirtualQuery(IntPtr address, out MemoryRegion region, IntPtr length);

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
        var audio = "Audio";
        var width = 0;
        var height = 0;
        var tracks = Player.Media?.Tracks;
        if (tracks is not null)
        {
            foreach (var track in tracks)
            {
                if (track.TrackType == TrackType.Video)
                {
                    var frame = track.Data.Video;
                    var sar = frame.SarDen == 0 ? 1d : frame.SarNum / (double)frame.SarDen;
                    width = (int)Math.Round(frame.Width * (sar <= 0 ? 1 : sar));
                    height = (int)frame.Height;
                    video = width > 0 ? $"{width}×{height}" : "Video";
                }
                else if (track.TrackType == TrackType.Audio && audio == "Audio")
                {
                    audio = "Audio";
                }
            }
        }

        return new PlaybackInfo(video, audio, width, height, Rate);
    }
}
