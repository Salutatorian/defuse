using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;
using Defuse.Application;
using Defuse.Domain;
using Defuse.Integrations;
using Defuse.Sources.Direct;

namespace Defuse.Desktop;

public partial class MainWindow
{
    private void PlaySaved(Guid mediaId)
    {
        var target = App.Library.Open(mediaId);
        if (target is null)
        {
            Status("That title is no longer in the library.");
            return;
        }

        var decision = App.Playback.Decide(target);
        var start = decision.Kind switch
        {
            ResumeKind.ResumeQuiet or ResumeKind.OfferResume => decision.PositionMs,
            ResumeKind.FromStart or ResumeKind.Completed or ResumeKind.Unseekable => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(decision), decision.Kind, null)
        };
        BeginPlayback(target, start, true);
    }

    private void BeginPlayback(PlayTarget target, long startMs, bool persist)
    {
        _playing = target;
        _persistPlay = persist;
        _mediaId = target.MediaId == Guid.Empty ? _mediaId : target.MediaId;
        _introEndMs = target.MediaId == Guid.Empty ? null : App.Library.Store.GetIntro(target.MediaId);
        _creditsMs = target.MediaId == Guid.Empty ? null : CreditMarks.Get(target.MediaId);
        _markedFinished = target.Progress?.Completed == true;
        var sceneLookup = ++_sceneLookup;
        _allowQueueAdvance = true;
        _nextEpisodeId = NextEpisode(target);
        _playerOpen = true;
        FailurePanel.Visibility = Visibility.Collapsed;
        PlayerSourcePanel.Visibility = Visibility.Collapsed;
        MorePanel.Visibility = Visibility.Collapsed;
        QueuePanel.Visibility = Visibility.Collapsed;
        PlayerReplacePanel.Visibility = Visibility.Collapsed;
        PlayerTitle.Text = LibraryController.DisplayTitle(target.Title);
        PlayerEpisode.Text = "";
        _chromeHeld = false;
        _subtitleMargin = -1;
        _subtitleTo = -1;
        _subtitleSlide?.Stop();
        _subtitleStyleLive = false;
        _subtitleStyleTries = 0;
        _awaitingPicture = true;
        _glassBlitFailed = false;
        _videoSurface = IntPtr.Zero;
        LoadingWash.Visibility = Visibility.Visible;
        SkipIntroButton.Visibility = Visibility.Collapsed;
        NextEpisodeButton.Visibility = Visibility.Collapsed;
        SyncQueueActions();
        _subsChosen = false;
        var look = PlaybackLook.Current;
        App.Engine.SetSubtitleStyle(look.FontSize, look.Color, look.TextOpacity, look.BackgroundOpacity, look.Outline);
        Shell.Visibility = Visibility.Collapsed;
        AddOverlay.Visibility = Visibility.Collapsed;
        SearchOverlay.Visibility = Visibility.Collapsed;
        if (Video.Parent == null)
            PlayerLayer.Children.Add(Video);
        Video.Visibility = Visibility.Visible;
        PlayerLayer.Visibility = Visibility.Visible;
        UseVideoSurface();
        UpdateLayout();
        AttachVideo();
        App.Engine.SetVolume((int)VolumeSlider.Value);
        App.Playback.Play(target, startMs, persist);
        if (_introEndMs is null || _creditsMs is null)
            _ = ApplySceneMarks(sceneLookup, target);
        ShowChrome();
        Dispatcher.BeginInvoke(() =>
        {
            if (!_playerOpen)
                return;
            Chrome.Focus();
        });
        _coveredWidth = 0;
        _coveredHeight = 0;
        _pictureW = 0;
        _pictureH = 0;
        PlayerLayer.SizeChanged -= VideoSized;
        PlayerLayer.SizeChanged += VideoSized;
        Dispatcher.BeginInvoke(EnsureVideo, DispatcherPriority.Render);
        StartStats();
    }

    private void AttachVideo()
    {
        if (!_playerOpen)
            return;
        if (Video.MediaPlayer != App.Engine.Player)
            Video.MediaPlayer = App.Engine.Player;
        BlackenVideo();
    }

    private void EnsureVideo()
    {
        if (!_playerOpen)
            return;
        if (App.Engine.Player.Hwnd == IntPtr.Zero)
        {
            Video.MediaPlayer = null;
            Video.MediaPlayer = App.Engine.Player;
        }
        BlackenVideo();
    }

    private static readonly IntPtr GreyBrush = CreateSolidBrush(0x00171717);

    private void BlackenVideo()
    {
        var hwnd = App.Engine.Player.Hwnd;
        if (hwnd == IntPtr.Zero)
            return;
        var brush = _awaitingPicture ? GreyBrush : GetStockObject(4);
        PaintWindow(hwnd, brush);
        EnumChildWindows(hwnd, (child, _) =>
        {
            PaintWindow(child, brush);
            return true;
        }, IntPtr.Zero);
    }

    private static void PaintWindow(IntPtr hwnd, IntPtr brush)
    {
        SetClassLongPtr(hwnd, -10, brush);
        if (!GetClientRect(hwnd, out var rect))
            return;
        var dc = GetDC(hwnd);
        if (dc == IntPtr.Zero)
            return;
        FillRect(dc, ref rect, brush);
        ReleaseDC(hwnd, dc);
    }

    private void LeavePlayer(object sender, RoutedEventArgs e)
    {
        if (!_playerOpen)
            return;
        _allowQueueAdvance = false;
        _sceneLookup++;
        _pauseOnRelease = false;
        _clickPauseTimer?.Stop();
        QueuePanel.Visibility = Visibility.Collapsed;
        NextEpisodeButton.Visibility = Visibility.Collapsed;
        RememberTrakt(paused: true);
        _sizeHold?.Stop();
        CancelFast();
        App.Playback.Pause();
        App.Playback.Stop();
        _playerOpen = false;
        _volumeArmed = false;
        Mouse.OverrideCursor = null;
        _chromeHeld = false;
        _subtitleMargin = -1;
        _subtitleTo = -1;
        _subtitleSlide?.Stop();
        _chromeTimer.Stop();
        _statsTimer?.Stop();
        HideChrome();
        PlayerLayer.SizeChanged -= VideoSized;
        Video.MediaPlayer = null;
        if (Video.Parent is Panel panel)
            panel.Children.Remove(Video);
        Video.Visibility = Visibility.Collapsed;
        PlayerLayer.Visibility = Visibility.Collapsed;
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window.Title == "LibVLCSharp.WPF")
                window.Hide();
        }
        PlayerTitle.Text = "";
        PlayerEpisode.Text = "";
        _awaitingPicture = false;
        LoadingWash.Visibility = Visibility.Collapsed;
        _seekTarget = null;
        SeekingMark.Visibility = Visibility.Collapsed;
        KeyGuide.Visibility = Visibility.Collapsed;
        _coveredWidth = 0;
        _coveredHeight = 0;
        _pictureW = 0;
        _pictureH = 0;
        _videoSurface = IntPtr.Zero;
        Shell.Visibility = Visibility.Visible;
        UseShellBackdrop();
        if (_fullScreen)
            ToggleFullScreen(this, e);
        if (_compact)
            ToggleCompact(this, e);
        if (_section == "library")
            ShowLibrary(this, e);
        else if (_section == "settings")
            ShowSettings(this, e);
        else
            ShowHome(this, e);
    }

    private void TogglePause(object sender, RoutedEventArgs e)
    {
        if (App.Engine.Current.IsPaused)
        {
            _chromeHeld = false;
            App.Playback.ResumePlayback();
            Flash("Play");
        }
        else
        {
            _chromeHeld = true;
            App.Playback.Pause();
            RememberTrakt(paused: true);
            Flash("Pause");
        }

        ShowChrome();
    }

    private void RetryPlayback(object sender, RoutedEventArgs e)
    {
        if (_playing is null)
            return;
        FailurePanel.Visibility = Visibility.Collapsed;
        var saved = _playing.MediaId == Guid.Empty || !_persistPlay
            ? _positionMs
            : App.Library.Store.GetProgress(App.Library.Store.ActiveProfileId, _playing.MediaId)?.PositionMs ?? _positionMs;
        BeginPlayback(_playing, saved, _persistPlay);
    }

    private void ShowPlayerSources(object sender, RoutedEventArgs e)
    {
        var rows = new List<Row>();
        if (_playing is PlayTarget show && !string.IsNullOrWhiteSpace(show.ShowTitle))
        {
            rows.AddRange(App.Library.Store.Episodes(show.ShowTitle)
                .OrderBy(item => item.SeasonNumber ?? 0)
                .ThenBy(item => item.EpisodeNumber ?? 0)
                .Select(ItemRow));
        }

        if (_playing?.MediaId is Guid id && id != Guid.Empty)
            rows.AddRange(SourceRows(id));
        if (rows.Count == 0)
        {
            Status("Save the title to see its sources.");
            return;
        }

        PlayerSourceList.ItemsSource = rows;
        MorePanel.Visibility = Visibility.Collapsed;
        PlayerSourcePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void ClosePlayerSources(object sender, RoutedEventArgs e) => PlayerSourcePanel.Visibility = Visibility.Collapsed;

    private void PlayPlayerSource(object sender, MouseButtonEventArgs e)
    {
        if (PlayerSourceList.SelectedItem is not Row row)
            return;
        if (row.MediaId is Guid episodeId)
        {
            PlayerSourcePanel.Visibility = Visibility.Collapsed;
            App.Playback.Stop();
            PlaySaved(episodeId);
            return;
        }

        if (_playing?.MediaId is not Guid mediaId || row.OtherId is not Guid sourceId)
            return;
        var target = App.Library.Store.OpenSource(mediaId, sourceId);
        if (target is null)
            return;
        var position = _positionMs;
        PlayerSourcePanel.Visibility = Visibility.Collapsed;
        App.Playback.Stop();
        BeginPlayback(target, position, true);
    }

    private void OpenPlayerReplace(object sender, RoutedEventArgs e)
    {
        PlayerReplaceUrl.Text = "";
        PlayerReplacePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void ClosePlayerReplace(object sender, RoutedEventArgs e) => PlayerReplacePanel.Visibility = Visibility.Collapsed;

    private void ApplyPlayerReplace(object sender, RoutedEventArgs e)
    {
        if (_playing?.MediaId is not Guid id || id == Guid.Empty)
        {
            Status("Save the title before replacing its link.");
            return;
        }

        try
        {
            var position = _positionMs;
            var replaced = App.Library.Replace(id, PlayerReplaceUrl.Text);
            PlayerReplacePanel.Visibility = Visibility.Collapsed;
            App.Playback.Stop();
            BeginPlayback(replaced, position, true);
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void OpenMore(object sender, RoutedEventArgs e)
    {
        MenuTitle.Text = "Playback";
        MenuHome.Visibility = Visibility.Visible;
        MenuBody.Visibility = Visibility.Collapsed;
        PlayerSourcePanel.Visibility = Visibility.Collapsed;
        MorePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void CloseMore(object sender, RoutedEventArgs e)
    {
        MorePanel.Visibility = Visibility.Collapsed;
        MenuBody.Visibility = Visibility.Collapsed;
        MenuHome.Visibility = Visibility.Collapsed;
        _openSection = "";
    }

    private void FocusAudio(object sender, RoutedEventArgs e) => ToggleSection("audio");
    private void FocusSubtitles(object sender, RoutedEventArgs e) => ToggleSection("subtitles");
    private void OpenSpeed(object sender, RoutedEventArgs e) => ToggleSection("speed");
    private void OpenChapters(object sender, RoutedEventArgs e) => ShowSection("chapters");
    private void OpenVideo(object sender, RoutedEventArgs e) => ShowSection("video");

    private string _openSection = "";
    private bool _subsChosen;

    private void ToggleSection(string section)
    {
        if (MorePanel.Visibility == Visibility.Visible && _openSection == section)
        {
            MorePanel.Visibility = Visibility.Collapsed;
            _openSection = "";
            return;
        }

        _openSection = section;
        ShowSection(section);
    }

    private void ShowSection(string section)
    {
        var info = App.Engine.Info;
        InfoText.Text = $"{info.Width}×{info.Height}   {info.Video}   {info.Audio}   {info.Rate:0.##}×";
        AudioList.ItemsSource = App.Engine.AudioTracks.Select(track => new Row(track.Name, Payload: track.Id.ToString(CultureInfo.InvariantCulture))).ToArray();
        var tracks = new List<Row> { new("Off", Payload: "-1") };
        tracks.AddRange(App.Engine.SubtitleTracks
            .Where(track => track.Id >= 0)
            .Select(track => new Row(track.Name, Payload: track.Id.ToString(CultureInfo.InvariantCulture))));
        SubtitleTrackList.ItemsSource = tracks;
        ChapterList.ItemsSource = App.Engine.Chapters.Select(chapter => new Row($"{chapter.Name}  {Format(chapter.OffsetMs)}", Payload: chapter.Index.ToString(CultureInfo.InvariantCulture))).ToArray();
        MenuTitle.Text = section switch
        {
            "audio" => "Audio",
            "subtitles" => "Subtitles",
            "speed" => "Speed",
            "chapters" => "Chapters",
            "video" => "Video",
            _ => "Playback"
        };
        InfoText.Visibility = section == "video" ? Visibility.Visible : Visibility.Collapsed;
        SubtitleNote.Visibility = section == "subtitles" ? Visibility.Visible : Visibility.Collapsed;
        RateList.Visibility = section == "speed" ? Visibility.Visible : Visibility.Collapsed;
        AudioList.Visibility = section == "audio" ? Visibility.Visible : Visibility.Collapsed;
        SubtitleTrackList.Visibility = section == "subtitles" ? Visibility.Visible : Visibility.Collapsed;
        SubtitleStyle.Visibility = section == "subtitles" ? Visibility.Visible : Visibility.Collapsed;
        if (section == "subtitles")
            ShowSubtitleLook();
        SubtitleDelayRow.Visibility = section == "subtitles" ? Visibility.Visible : Visibility.Collapsed;
        ChapterList.Visibility = section == "chapters" ? Visibility.Visible : Visibility.Collapsed;
        StartOverButton.Visibility = section == "video" ? Visibility.Visible : Visibility.Collapsed;
        IntroMarkButton.Visibility = section == "video" ? Visibility.Visible : Visibility.Collapsed;
        CreditsMarkButton.Visibility = section == "video" ? Visibility.Visible : Visibility.Collapsed;
        MenuHome.Visibility = Visibility.Collapsed;
        MenuBody.Visibility = Visibility.Visible;
        PlayerSourcePanel.Visibility = Visibility.Collapsed;
        MorePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void SubtitleDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!_playerOpen || !IsLoaded)
            return;
        App.Playback.SetSubtitleDelay((long)e.NewValue);
    }

    private void PlayPrevious(object sender, RoutedEventArgs e)
    {
        if (_positionMs > 3_000)
        {
            CommitSeek(0);
            return;
        }

        if (_playing is not null && PreviousEpisode(_playing) is Guid id)
        {
            App.Playback.Stop();
            PlaySaved(id);
            return;
        }

        CommitSeek(0);
    }

    private const long SkipMs = 10_000;
    private bool _awaitingPicture;
    private long? _seekTarget;
    private long _pendingSeek = -1;
    private int _seekTries;
    private long _seekUntil;
    private long _seekOrigin;
    private double _volumeBeforeMute = 100;

    private void SkipBackward(object sender, RoutedEventArgs e) => Jump(-SkipMs);

    private void SkipForward(object sender, RoutedEventArgs e) => Jump(SkipMs);

    private void Jump(long delta)
    {
        var origin = _seekTarget ?? _positionMs;
        CommitSeek(origin + delta);
    }

    private void CommitSeek(long position)
    {
        if (!_playerOpen)
            return;
        if (_durationMs > 0)
            position = Math.Clamp(position, 0, Math.Max(0, _durationMs - 400));
        else
            position = Math.Max(0, position);
        _seekOrigin = _seekTarget ?? _positionMs;
        _seekTarget = position;
        _seekTries = 0;
        _seekUntil = Environment.TickCount64 + 1600;
        _positionMs = position;
        SeekingMark.Visibility = Visibility.Visible;
        TimeCurrent.Text = Format(position);
        App.Playback.Seek(position, _durationMs);
    }

    private void FinishSliderSeek()
    {
        if (!_playerOpen || _pendingSeek < 0)
            return;
        CommitSeek(_pendingSeek);
        if (!SeekBar.IsMouseCaptureWithin)
            SeekHint.Visibility = Visibility.Collapsed;
    }

    private void ApplyRate(object sender, SelectionChangedEventArgs e)
    {
        if (!_playerOpen || RateList.SelectedItem is not string label)
            return;
        var number = label.TrimEnd('×');
        if (float.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var rate))
        {
            _fastFromHold = false;
            _fastFromSpace = false;
            _rateBeforeFast = rate;
            App.Playback.SetRate(rate);
        }
    }

    private void ApplyAudio(object sender, SelectionChangedEventArgs e)
    {
        if (!_playerOpen || AudioList.SelectedItem is not Row row)
            return;
        if (int.TryParse(row.Payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            App.Playback.SelectAudio(id);
    }

    private void ApplySubtitleTrack(object sender, SelectionChangedEventArgs e)
    {
        if (!_playerOpen || SubtitleTrackList.SelectedItem is not Row row)
            return;
        if (!int.TryParse(row.Payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            return;
        var look = PlaybackLook.Current;
        look.SubtitlesOn = id >= 0;
        look.Language = id >= 0 ? row.Label : look.Language;
        look.Save();
        App.Playback.SelectSubtitle(id);
    }

    private DispatcherTimer? _sizeHold;
    private int _sizeDirection = 1;
    private bool _colorWrite;

    private void SizeHoldStart(object sender, MouseButtonEventArgs e)
    {
        _sizeDirection = ReadTag(sender);
        if (_sizeDirection == 0)
            _sizeDirection = 1;
        NudgeSubtitleSize();
        _sizeHold ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(420) };
        _sizeHold.Interval = TimeSpan.FromMilliseconds(420);
        _sizeHold.Tick -= SizeHoldTick;
        _sizeHold.Tick += SizeHoldTick;
        _sizeHold.Start();
        e.Handled = true;
    }

    private void SizeHoldStop(object sender, MouseButtonEventArgs e) => EndSizeHold();

    private void SizeHoldLeave(object sender, MouseEventArgs e) => EndSizeHold();

    private void EndSizeHold()
    {
        if (_sizeHold?.IsEnabled != true)
            return;
        _sizeHold.Stop();
        PlaybackLook.Current.Save();
    }

    private void SizeHoldTick(object? sender, EventArgs e)
    {
        if (Mouse.LeftButton != MouseButtonState.Pressed)
        {
            _sizeHold?.Stop();
            PlaybackLook.Current.Save();
            return;
        }

        if (_sizeHold is not null)
            _sizeHold.Interval = TimeSpan.FromMilliseconds(140);
        NudgeSubtitleSize();
    }

    private void NudgeSubtitleSize()
    {
        var look = PlaybackLook.Current;
        var size = Math.Clamp(look.FontSize + _sizeDirection, 1, 2000);
        if (size == look.FontSize)
            return;
        look.FontSize = size;
        SubtitleSizeLabel.Text = size.ToString(CultureInfo.InvariantCulture);
        App.Engine.SetSubtitleStyle(size, look.Color, look.TextOpacity, 0, 0);
    }

    private void SubtitleSizeTyping(object sender, TextCompositionEventArgs e)
    {
        e.Handled = e.Text.Any(ch => ch is < '0' or > '9');
    }

    private void SubtitleSizeKey(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;
        CommitSubtitleSize();
        Chrome.Focus();
        e.Handled = true;
    }

    private void SubtitleSizeCommit(object sender, RoutedEventArgs e) => CommitSubtitleSize();

    private void CommitSubtitleSize()
    {
        var look = PlaybackLook.Current;
        if (!int.TryParse(SubtitleSizeLabel.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size))
        {
            SubtitleSizeLabel.Text = look.FontSize.ToString(CultureInfo.InvariantCulture);
            return;
        }

        size = Math.Clamp(size, 1, 2000);
        SubtitleSizeLabel.Text = size.ToString(CultureInfo.InvariantCulture);
        if (size == look.FontSize)
            return;
        look.FontSize = size;
        look.Save();
    }

    private static int ReadTag(object sender) =>
        sender is FrameworkElement { Tag: string text } && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private void ShowSubtitleLook()
    {
        var look = PlaybackLook.Current;
        SubtitleSizeLabel.Text = look.FontSize.ToString(CultureInfo.InvariantCulture);
        var (hue, saturation) = HueOf(look.Color);
        _colorWrite = true;
        SubtitleHue.Value = hue;
        SubtitleSaturation.Value = saturation;
        _colorWrite = false;
        PaintSwatch(look.Color);
    }

    private void SubtitleHueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_colorWrite || !IsLoaded)
            return;
        if (SubtitleSaturation.Value < 8)
        {
            _colorWrite = true;
            SubtitleSaturation.Value = 100;
            _colorWrite = false;
        }

        ApplyHue(save: false);
    }

    private void SubtitleSaturationChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_colorWrite || !IsLoaded)
            return;
        ApplyHue(save: false);
    }

    private void SubtitleColorCommit(object sender, MouseButtonEventArgs e) => PlaybackLook.Current.Save();

    private void ApplyHue(bool save)
    {
        var color = ColorFromHue(SubtitleHue.Value, SubtitleSaturation.Value / 100d);
        var look = PlaybackLook.Current;
        look.Color = color;
        look.BackgroundOpacity = 0;
        look.Outline = 0;
        PaintSwatch(color);
        if (save)
            look.Save();
        else
            App.Engine.SetSubtitleStyle(look.FontSize, color, look.TextOpacity, 0, 0, immediate: false);
    }

    private void PaintSwatch(int color)
    {
        SubtitleSwatch.Background = new SolidColorBrush(Color.FromRgb(
            (byte)((color >> 16) & 255),
            (byte)((color >> 8) & 255),
            (byte)(color & 255)));
    }

    private static (double Hue, double Saturation) HueOf(int color)
    {
        var r = ((color >> 16) & 255) / 255d;
        var g = ((color >> 8) & 255) / 255d;
        var b = (color & 255) / 255d;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        if (delta < 0.001 || max < 0.001)
            return (0, 0);
        double hue = max == r
            ? 60 * (((g - b) / delta) % 6)
            : max == g
                ? 60 * (((b - r) / delta) + 2)
                : 60 * (((r - g) / delta) + 4);
        if (hue < 0)
            hue += 360;
        return (hue, delta / max * 100d);
    }

    private static int ColorFromHue(double hue, double saturation)
    {
        saturation = Math.Clamp(saturation, 0, 1);
        hue = ((hue % 360d) + 360d) % 360d;
        var chroma = saturation;
        var sector = hue / 60d;
        var x = chroma * (1d - Math.Abs(sector % 2d - 1d));
        double r;
        double g;
        double b;
        switch ((int)Math.Floor(sector) % 6)
        {
            case 0: r = chroma; g = x; b = 0; break;
            case 1: r = x; g = chroma; b = 0; break;
            case 2: r = 0; g = chroma; b = x; break;
            case 3: r = 0; g = x; b = chroma; break;
            case 4: r = x; g = 0; b = chroma; break;
            default: r = chroma; g = 0; b = x; break;
        }
        var lift = 1d - chroma;
        return ((int)Math.Round((r + lift) * 255d) << 16)
            | ((int)Math.Round((g + lift) * 255d) << 8)
            | (int)Math.Round((b + lift) * 255d);
    }

    private void ApplyChapter(object sender, MouseButtonEventArgs e)
    {
        if (ChapterList.SelectedItem is Row row && int.TryParse(row.Payload, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            App.Playback.SetChapter(id);
    }

    private void StartOver(object sender, RoutedEventArgs e)
    {
        CommitSeek(0);
        Flash("Start");
    }

    private void MarkIntroFromPlayer(object sender, RoutedEventArgs e)
    {
        if (_playing?.MediaId is not Guid id || id == Guid.Empty)
            return;
        App.Library.Store.SetIntro(id, _positionMs);
        _introEndMs = _positionMs;
        Status("Intro end saved at the current time.");
    }

    private void MarkCredits(object sender, RoutedEventArgs e)
    {
        if (_playing?.MediaId is not Guid id || id == Guid.Empty)
            return;
        CreditMarks.Set(id, _positionMs);
        _creditsMs = _positionMs;
        Status("Credits saved at the current time.");
    }

    private void SkipIntro(object sender, RoutedEventArgs e)
    {
        if (_introEndMs is long end)
            CommitSeek(end);
    }

    private async Task ApplySceneMarks(int lookup, PlayTarget target)
    {
        try
        {
            var marks = await SceneGuide.Lookup(
                App.Http, target.ShowTitle, target.SeasonNumber, target.EpisodeNumber, target.Title, CancellationToken.None);
            await Dispatcher.InvokeAsync(() =>
            {
                if (lookup != _sceneLookup || !_playerOpen || _playing?.MediaId != target.MediaId)
                    return;
                if (_introEndMs is null && marks.IntroEndMs is long intro && intro > 1000)
                {
                    _introEndMs = intro;
                    if (target.MediaId != Guid.Empty)
                        App.Library.Store.SetIntro(target.MediaId, intro);
                }

                if (_creditsMs is null && marks.CreditsStartMs is long credits && credits > 1000)
                {
                    _creditsMs = credits;
                    if (target.MediaId != Guid.Empty)
                        CreditMarks.Set(target.MediaId, credits);
                }
            });
        }
        catch (Exception)
        {
        }
    }

    private void OpenQueue(object sender, RoutedEventArgs e)
    {
        QueueRows.Children.Clear();
        string? show = null;
        var mixed = false;
        var number = 1;
        foreach (var id in WatchQueue.Ids)
        {
            var target = App.Library.Open(id);
            if (target is null)
            {
                number++;
                continue;
            }

            if (show is null)
                show = target.ShowTitle;
            else if (!string.Equals(show, target.ShowTitle, StringComparison.OrdinalIgnoreCase))
                mixed = true;

            var playing = _playing?.MediaId == id;
            var name = target.Title;
            var detail = target.EpisodeNumber is int episode
                ? target.SeasonNumber is int season ? $"Season {season}  ·  Episode {episode}" : $"Episode {episode}"
                : "";
            var body = new StackPanel { Margin = new Thickness(0, 8, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, MaxWidth = 380 };
            body.Children.Add(new TextBlock
            {
                Text = number + "    " + name,
                Foreground = Brushes.White,
                FontSize = 16,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Left,
                HorizontalAlignment = HorizontalAlignment.Left
            });
            if (detail.Length > 0)
                body.Children.Add(new TextBlock { Text = detail, Foreground = Brushes.White, Opacity = 0.7, FontSize = 12, Margin = new Thickness(28, 2, 0, 0) });
            if (playing)
                body.Children.Add(new TextBlock { Text = "Now playing", Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(28, 4, 0, 0) });
            var row = new Button
            {
                Content = body,
                Tag = id.ToString(),
                Style = (Style)FindResource("QueueRow"),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(0),
                Margin = new Thickness(0)
            };
            row.Click += PlayQueueItem;
            QueueRows.Children.Add(row);
            number++;
        }

        QueueHeading.Text = !mixed && !string.IsNullOrWhiteSpace(show) ? show : "Queue";
        QueuePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void CloseQueue(object sender, RoutedEventArgs e) => QueuePanel.Visibility = Visibility.Collapsed;

    private void PlayQueueItem(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || !Guid.TryParse(text, out var id))
            return;
        _allowQueueAdvance = false;
        if (_playerOpen)
            App.Playback.Stop();
        PlaySaved(id);
    }

    private void PlayQueueNext(object sender, RoutedEventArgs e)
    {
        if (_playing is null || WatchQueue.Next(_playing.MediaId) is not Guid id)
            return;
        _allowQueueAdvance = false;
        if (_playerOpen)
            App.Playback.Stop();
        PlaySaved(id);
    }

    private void OnPlaybackEnded()
    {
        App.Playback.Flush();
        if (!_allowQueueAdvance || !_playerOpen || _playing is null)
            return;
        if (WatchQueue.Next(_playing.MediaId) is not Guid id)
            return;
        _allowQueueAdvance = false;
        PlaySaved(id);
    }

    private void SyncQueueActions()
    {
        var queued = WatchQueue.Ids.Count > 1 && _playing is not null && WatchQueue.Ids.Contains(_playing.MediaId);
        var hasNext = _playing is not null && WatchQueue.Next(_playing.MediaId) is not null;
        QueueActions.Visibility = queued ? Visibility.Visible : Visibility.Collapsed;
        QueueSkip.Visibility = hasNext ? Visibility.Visible : Visibility.Collapsed;
        QueueOpen.Visibility = queued ? Visibility.Visible : Visibility.Collapsed;
        if (!queued)
            QueuePanel.Visibility = Visibility.Collapsed;
    }

    private void PlayNext(object sender, RoutedEventArgs e)
    {
        if (_nextEpisodeId is Guid id)
        {
            if (_playerOpen)
                App.Playback.Stop();
            PlaySaved(id);
        }
    }

    private void ToggleCompact(object sender, RoutedEventArgs e)
    {
        if (_fullScreen)
            ToggleFullScreen(sender, e);
        if (_compact)
            LeaveCompact();
        else
            EnterCompact();
    }

    private void EnterCompact()
    {
        _preCompactBounds = WindowState == WindowState.Maximized
            ? RestoreBounds
            : new Rect(Left, Top, Width, Height);
        if (WindowState == WindowState.Maximized)
            WindowState = WindowState.Normal;
        var aspect = _preCompactBounds.Height > 1 ? _preCompactBounds.Width / _preCompactBounds.Height : 16d / 9d;
        if (aspect < 0.4 || aspect > 4)
            aspect = 16d / 9d;
        _compactAspect = aspect;
        _compact = true;
        Topmost = true;
        MinWidth = 340;
        MinHeight = Math.Max(190, 340 / aspect);
        var width = Math.Min(480, Math.Max(MinWidth, _preCompactBounds.Width));
        Width = width;
        Height = Math.Max(MinHeight, width / aspect);
        UpdateLayout();
        FitCompactBar();
        _subtitleMargin = -1;
        FillVideo();
    }

    private void LeaveCompact()
    {
        _compact = false;
        Topmost = _fullScreen;
        MinWidth = 920;
        MinHeight = 600;
        PlayerBar.LayoutTransform = Transform.Identity;
        QueuePanel.Width = 420;
        StatsPanel.MinWidth = 560;
        StatsPanel.ClearValue(FrameworkElement.MaxWidthProperty);
        if (_preCompactBounds.Width > 1 && _preCompactBounds.Height > 1)
        {
            Left = _preCompactBounds.Left;
            Top = _preCompactBounds.Top;
            Width = _preCompactBounds.Width;
            Height = _preCompactBounds.Height;
        }

        UpdateLayout();
        _subtitleMargin = -1;
        if (_playerOpen)
            FillVideo();
    }

    private void FitCompactBar()
    {
        if (!_compact)
        {
            if (PlayerBar.LayoutTransform is ScaleTransform)
                PlayerBar.LayoutTransform = Transform.Identity;
            return;
        }

        var available = Math.Max(160, ActualWidth - 24);
        var scale = Math.Min(1, available / 860d);
        if (PlayerBar.LayoutTransform is not ScaleTransform current || Math.Abs(current.ScaleX - scale) > 0.01)
            PlayerBar.LayoutTransform = new ScaleTransform(scale, scale);
        QueuePanel.Width = Math.Max(180, Math.Min(420, ActualWidth - 48));
        StatsPanel.MinWidth = 0;
        StatsPanel.MaxWidth = Math.Max(180, ActualWidth - 32);
    }

    private void LockCompactRect(ref NativeRect rect, int edge)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var minW = Math.Max(1, (int)Math.Round(MinWidth * scaleX));
        var minH = Math.Max(1, (int)Math.Round(MinHeight * scaleY));
        var aspect = _compactAspect > 0.2 ? _compactAspect : 16d / 9d;
        var left = rect.Left;
        var top = rect.Top;
        var right = rect.Right;
        var bottom = rect.Bottom;
        int width;
        int height;
        if (edge is 3 or 6)
        {
            height = Math.Max(minH, bottom - top);
            width = Math.Max(minW, (int)Math.Round(height * aspect));
            height = Math.Max(minH, (int)Math.Round(width / aspect));
        }
        else
        {
            width = Math.Max(minW, right - left);
            height = Math.Max(minH, (int)Math.Round(width / aspect));
            width = Math.Max(minW, (int)Math.Round(height * aspect));
        }

        switch (edge)
        {
            case 1:
            case 4:
            case 7:
                rect.Left = right - width;
                rect.Right = right;
                break;
            default:
                rect.Left = left;
                rect.Right = left + width;
                break;
        }

        switch (edge)
        {
            case 3:
            case 4:
            case 5:
                rect.Top = bottom - height;
                rect.Bottom = bottom;
                break;
            default:
                rect.Top = top;
                rect.Bottom = top + height;
                break;
        }
    }

    private void ToggleFullScreen(object sender, RoutedEventArgs e)
    {
        var chrome = WindowChrome.GetWindowChrome(this);
        if (!_fullScreen)
        {
            _savedState = WindowState;
            _savedBounds = WindowState == WindowState.Maximized
                ? RestoreBounds
                : new Rect(Left, Top, Width, Height);
            WindowState = WindowState.Normal;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 0;
                chrome.ResizeBorderThickness = new Thickness(0);
            }

            var screen = FullMonitorBounds();
            Topmost = true;
            Left = screen.Left - 2;
            Top = screen.Top - 2;
            Width = screen.Width + 4;
            Height = screen.Height + 4;
            if (chrome is not null)
                chrome.CornerRadius = new CornerRadius(0);
            Tint.CornerRadius = new CornerRadius(0);
            Tint.BorderThickness = new Thickness(0);
            Tint.BorderBrush = Brushes.Transparent;
            WindowBackdrop.SetSquare(this, true);
            ConcealFullscreenEdge();
            _fullScreen = true;
            _coveredWidth = 0;
            Dispatcher.BeginInvoke(() =>
            {
                if (_fullScreen)
                    ConcealFullscreenEdge();
                FillVideo();
            }, DispatcherPriority.Loaded);
        }
        else
        {
            Topmost = _compact;
            WindowState = WindowState.Normal;
            Left = _savedBounds.Left;
            Top = _savedBounds.Top;
            Width = _savedBounds.Width;
            Height = _savedBounds.Height;
            if (chrome is not null)
            {
                chrome.CaptionHeight = 48;
                chrome.ResizeBorderThickness = new Thickness(10);
                chrome.CornerRadius = new CornerRadius(0);
            }

            Tint.CornerRadius = new CornerRadius(12);
            Tint.BorderBrush = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
            Tint.BorderThickness = new Thickness(1);
            WindowBackdrop.SetSquare(this, false);
            WindowBackdrop.HideBorder(this, false);

            if (_savedState == WindowState.Maximized)
                WindowState = WindowState.Maximized;
            _fullScreen = false;
            _coveredWidth = 0;
            SyncPlayCursor();
            Dispatcher.BeginInvoke(FillVideo, DispatcherPriority.Loaded);
        }
    }

    private void ConcealFullscreenEdge()
    {
        WindowBackdrop.HideBorder(this, true);
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window.Title == "LibVLCSharp.WPF")
                WindowBackdrop.HideBorder(window, true);
        }
    }

    private Rect FullMonitorBounds()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var monitor = MonitorFromWindow(hwnd, 2);
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
            return new Rect(0, 0, SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);

        var source = PresentationSource.FromVisual(this);
        var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var topLeft = toDip.Transform(new Point(info.Monitor.Left, info.Monitor.Top));
        var bottomRight = toDip.Transform(new Point(info.Monitor.Right, info.Monitor.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr hwnd, EnumWindowsProc proc, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateSolidBrush(uint color);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr(IntPtr hwnd, int index, IntPtr value);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern IntPtr GetParent(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern int FillRect(IntPtr dc, ref NativeRect rect, IntPtr brush);

    [DllImport("gdi32.dll")]
    private static extern IntPtr GetStockObject(int obj);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr obj);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool StretchBlt(IntPtr dest, int dx, int dy, int dw, int dh, IntPtr src, int sx, int sy, int sw, int sh, int rop);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(IntPtr dc, IntPtr bmp, uint start, uint lines, byte[] bits, ref BitmapInfo info, uint usage);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hwnd, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);

    [DllImport("gdi32.dll")]
    private static extern bool SetViewportOrgEx(IntPtr hdc, int x, int y, IntPtr previous);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    private bool _glassBlitFailed;

    private void StartGlass()
    {
        PlayerBlur.Background = Brushes.Transparent;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    private void PaintGlass()
    {
        if (PlayerPlate.ActualWidth < 2 || PlayerPlate.ActualHeight < 2)
            return;
        var hwnd = VideoSurface();
        if (hwnd == IntPtr.Zero)
            return;
        var origin = PlayerPlate.PointToScreen(new Point(0, 0));
        var far = PlayerPlate.PointToScreen(new Point(PlayerPlate.ActualWidth, PlayerPlate.ActualHeight));
        var topLeft = new NativePoint { X = (int)Math.Round(origin.X), Y = (int)Math.Round(origin.Y) };
        var bottomRight = new NativePoint { X = (int)Math.Round(far.X), Y = (int)Math.Round(far.Y) };
        ScreenToClient(hwnd, ref topLeft);
        ScreenToClient(hwnd, ref bottomRight);
        var shot = BlurWindow(hwnd, topLeft.X, topLeft.Y, Math.Max(1, bottomRight.X - topLeft.X), Math.Max(1, bottomRight.Y - topLeft.Y));
        if (shot is not null)
            PlayerBlur.Background = new ImageBrush(shot) { Stretch = Stretch.Fill };
    }

    private IntPtr _videoSurface;

    private IntPtr VideoSurface()
    {
        if (_videoSurface != IntPtr.Zero && GetClientRect(_videoSurface, out var cached) && cached.Right > 0 && cached.Bottom > 0)
            return _videoSurface;
        var hwnd = App.Engine.Player.Hwnd;
        if (hwnd == IntPtr.Zero)
            return IntPtr.Zero;
        var best = hwnd;
        var bestArea = 0;
        if (GetClientRect(hwnd, out var parent))
            bestArea = parent.Right * parent.Bottom;
        EnumChildWindows(hwnd, (child, _) =>
        {
            if (GetClientRect(child, out var rect))
            {
                var area = rect.Right * rect.Bottom;
                if (area > bestArea)
                {
                    bestArea = area;
                    best = child;
                }
            }

            return true;
        }, IntPtr.Zero);
        _videoSurface = best;
        return best;
    }

    private BitmapSource? BlurWindow(IntPtr hwnd, int x, int y, int width, int height)
    {
        var sw = 0;
        var sh = 0;
        byte[]? buffer = null;
        if (!_glassBlitFailed)
        {
            buffer = ReadRegion(hwnd, x, y, width, height, blit: true, out sw, out sh);
            if (buffer is not null && WashedOut(buffer))
            {
                _glassBlitFailed = true;
                buffer = null;
            }
        }

        if (buffer is null)
        {
            buffer = ReadRegion(hwnd, x, y, width, height, blit: false, out sw, out sh);
            if (buffer is null || WashedOut(buffer))
                return null;
        }
        else if (WashedOut(buffer))
        {
            return null;
        }

        BoxBlur(buffer, sw, sh);
        var source = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, buffer, sw * 4);
        source.Freeze();
        return source;
    }

    private static bool WashedOut(byte[] pixels)
    {
        long sum = 0;
        long spread = 0;
        var samples = 0;
        for (var i = 0; i < pixels.Length; i += 16)
        {
            var level = pixels[i] + pixels[i + 1] + pixels[i + 2];
            sum += level;
            spread += Math.Abs(pixels[i] - pixels[i + 2]);
            samples++;
        }

        if (samples == 0)
            return true;
        var average = sum / samples;
        return average < 8 || average > 640 || spread / samples < 4;
    }

    private static byte[]? ReadRegion(IntPtr hwnd, int x, int y, int width, int height, bool blit, out int sw, out int sh)
    {
        sw = Math.Max(24, width / 4);
        sh = Math.Max(12, height / 4);
        var window = GetDC(hwnd);
        var mem = CreateCompatibleDC(window);
        var dib = CreateCompatibleBitmap(window, blit ? sw : width, blit ? sh : height);
        var old = SelectObject(mem, dib);
        if (blit)
            StretchBlt(mem, 0, 0, sw, sh, window, x, y, width, height, 0x00CC0020);
        else
        {
            SetViewportOrgEx(mem, -x, -y, IntPtr.Zero);
            PrintWindow(hwnd, mem, 2);
            SetViewportOrgEx(mem, 0, 0, IntPtr.Zero);
        }

        var readWidth = blit ? sw : width;
        var readHeight = blit ? sh : height;
        var raw = ReadDib(mem, dib, readWidth, readHeight);
        SelectObject(mem, old);
        DeleteObject(dib);
        DeleteDC(mem);
        ReleaseDC(hwnd, window);
        if (raw is null || blit)
            return raw;
        var small = new byte[sw * sh * 4];
        for (var row = 0; row < sh; row++)
        {
            var sy = row * height / sh;
            for (var col = 0; col < sw; col++)
            {
                var sx = col * width / sw;
                var from = (sy * width + sx) * 4;
                var to = (row * sw + col) * 4;
                small[to] = raw[from];
                small[to + 1] = raw[from + 1];
                small[to + 2] = raw[from + 2];
                small[to + 3] = 255;
            }
        }

        return small;
    }

    private static byte[]? ReadDib(IntPtr dc, IntPtr dib, int width, int height)
    {
        var stride = width * 4;
        var buffer = new byte[stride * height];
        var info = new BitmapInfo
        {
            Header = new BitmapInfoHeader
            {
                Size = Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32
            }
        };
        return GetDIBits(dc, dib, 0, (uint)height, buffer, ref info, 0) == 0 ? null : buffer;
    }

    private static void BoxBlur(byte[] pixels, int w, int h)
    {
        var next = new byte[pixels.Length];
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var b = 0;
                var g = 0;
                var r = 0;
                var n = 0;
                for (var ky = -2; ky <= 2; ky++)
                {
                    var yy = y + ky;
                    if ((uint)yy >= (uint)h)
                        continue;
                    for (var kx = -2; kx <= 2; kx++)
                    {
                        var xx = x + kx;
                        if ((uint)xx >= (uint)w)
                            continue;
                        var i = (yy * w + xx) * 4;
                        b += pixels[i];
                        g += pixels[i + 1];
                        r += pixels[i + 2];
                        n++;
                    }
                }

                var o = (y * w + x) * 4;
                next[o] = (byte)(b / n);
                next[o + 1] = (byte)(g / n);
                next[o + 2] = (byte)(r / n);
                next[o + 3] = 255;
            }
        }

        Buffer.BlockCopy(next, 0, pixels, 0, pixels.Length);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    private int _coveredWidth;
    private int _coveredHeight;

    private void VideoSized(object sender, SizeChangedEventArgs e) => FillVideo();

    private void FillVideo()
    {
        if (!_playerOpen || Video.ActualWidth < 2 || Video.ActualHeight < 2)
            return;
        var hwnd = App.Engine.Player.Hwnd;
        if (hwnd == IntPtr.Zero)
            return;

        Video.ClearValue(FrameworkElement.WidthProperty);
        Video.ClearValue(FrameworkElement.HeightProperty);
        Video.HorizontalAlignment = HorizontalAlignment.Stretch;
        Video.VerticalAlignment = VerticalAlignment.Stretch;

        var dpi = VisualTreeHelper.GetDpi(this);
        var width = Math.Max(1, (int)Math.Round(Video.ActualWidth * dpi.DpiScaleX));
        var height = Math.Max(1, (int)Math.Round(Video.ActualHeight * dpi.DpiScaleY));
        if (width == _coveredWidth && height == _coveredHeight
            && GetClientRect(hwnd, out var current) && current.Right == width && current.Bottom == height)
            return;

        // VLC sizes the picture from this window. A WPF layout change does not reach it on its own.
        var origin = Video.PointToScreen(new Point(0, 0));
        var parent = GetParent(hwnd);
        var corner = new NativePoint { X = (int)Math.Round(origin.X), Y = (int)Math.Round(origin.Y) };
        var flags = 0x0004u | 0x0010u;
        if (parent == IntPtr.Zero)
            flags |= 0x0002u;
        else
            ScreenToClient(parent, ref corner);
        SetWindowPos(hwnd, IntPtr.Zero, corner.X, corner.Y, width, height, flags);
        _coveredWidth = width;
        _coveredHeight = height;
        App.Playback.Fit();
        PlaceSubtitles(PlayerBar.Opacity > 0);
    }

    private bool _draggingPlayer;
    private Point _dragCursor;
    private Point _dragOrigin;

    private void BeginCornerResize(object sender, MouseButtonEventArgs e)
    {
        if (_fullScreen || WindowState != WindowState.Normal || sender is not FrameworkElement grip || grip.Tag is not string corner)
            return;
        var start = grip.PointToScreen(e.GetPosition(grip));
        var originLeft = Left;
        var originTop = Top;
        var originWidth = Width;
        var originHeight = Height;
        grip.CaptureMouse();
        grip.MouseMove += Move;
        grip.MouseLeftButtonUp += Up;
        e.Handled = true;

        void Move(object s, MouseEventArgs args)
        {
            if (args.LeftButton != MouseButtonState.Pressed)
            {
                Finish();
                return;
            }

            var now = grip.PointToScreen(args.GetPosition(grip));
            var source = PresentationSource.FromVisual(this);
            var delta = (source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity).Transform(new Vector(now.X - start.X, now.Y - start.Y));
            var width = originWidth;
            var height = originHeight;
            var left = originLeft;
            var top = originTop;
            if (corner is "tl" or "bl")
            {
                width = Math.Max(MinWidth, originWidth - delta.X);
                left = originLeft + (originWidth - width);
            }
            else
            {
                width = Math.Max(MinWidth, originWidth + delta.X);
            }

            if (corner is "tl" or "tr")
            {
                height = Math.Max(MinHeight, originHeight - delta.Y);
                top = originTop + (originHeight - height);
            }
            else
            {
                height = Math.Max(MinHeight, originHeight + delta.Y);
            }

            if (_compact && _compactAspect > 0.2)
            {
                var widthPull = Math.Abs(width - originWidth);
                var heightPull = Math.Abs(height - originHeight);
                if (widthPull >= heightPull)
                    height = Math.Max(MinHeight, width / _compactAspect);
                else
                    width = Math.Max(MinWidth, height * _compactAspect);
                height = Math.Max(MinHeight, width / _compactAspect);
                if (corner is "tl" or "bl")
                    left = originLeft + (originWidth - width);
                if (corner is "tl" or "tr")
                    top = originTop + (originHeight - height);
            }

            Left = left;
            Top = top;
            Width = width;
            Height = height;
            UpdateLayout();
            FillVideo();
        }

        void Finish()
        {
            grip.MouseMove -= Move;
            grip.MouseLeftButtonUp -= Up;
            grip.ReleaseMouseCapture();
        }

        void Up(object s, MouseButtonEventArgs args) => Finish();
    }

    private void BeginPlayerDrag(object sender, MouseButtonEventArgs e)
    {
        if (_fullScreen || e.LeftButton != MouseButtonState.Pressed || sender is not UIElement element)
            return;
        if (IsPlayerControl(e.OriginalSource as DependencyObject))
            return;
        _dragCursor = element.PointToScreen(e.GetPosition(element));
        _dragOrigin = new Point(Left, Top);
        _draggingPlayer = true;
        element.CaptureMouse();
        e.Handled = true;
    }

    private void MovePlayerDrag(object sender, MouseEventArgs e)
    {
        if (!_draggingPlayer || e.LeftButton != MouseButtonState.Pressed || sender is not UIElement element)
            return;
        var now = element.PointToScreen(e.GetPosition(element));
        var source = PresentationSource.FromVisual(element);
        var fromDevice = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
        var delta = fromDevice.Transform(new Vector(now.X - _dragCursor.X, now.Y - _dragCursor.Y));
        Left = _dragOrigin.X + delta.X;
        Top = _dragOrigin.Y + delta.Y;
    }

    private void EndPlayerDrag(object sender, MouseButtonEventArgs e)
    {
        _draggingPlayer = false;
        if (sender is UIElement element)
            element.ReleaseMouseCapture();
    }

    private static bool IsPlayerControl(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button or Slider or TextBox or ListBox or RepeatButton)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private bool IsInsideBar(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source == PlayerBar)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private Point _chromeCursor;

    private void ChromeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (!_playerOpen || e.ChangedButton != MouseButton.Left)
            return;
        if (e.ClickCount == 2)
        {
            _volumeArmed = false;
            _pauseOnRelease = false;
            _clickPauseTimer?.Stop();
            CancelFastHold();
            if (_fastFromHold)
                EndFast(hold: true);
            if (IsPlayerControl(e.OriginalSource as DependencyObject) || IsPlayerPanel(e.OriginalSource as DependencyObject) || IsInsideBar(e.OriginalSource as DependencyObject))
                return;
            ToggleFullScreen(this, e);
            e.Handled = true;
            return;
        }

        if (IsPlayerControl(e.OriginalSource as DependencyObject) || IsPlayerPanel(e.OriginalSource as DependencyObject) || IsInsideBar(e.OriginalSource as DependencyObject))
            return;
        if (PointerBand(e) > 0)
        {
            _volumeArmed = true;
            _volumeStart = e.GetPosition(Chrome);
            _volumeOrigin = VolumeSlider.Value;
            return;
        }

        if (PointerBand(e) == 0)
        {
            _pauseOnRelease = true;
            ArmFastHold(false);
        }
    }

    private void ChromePressUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;
        _volumeArmed = false;
        var pause = _pauseOnRelease && !_fastFromHold;
        _pauseOnRelease = false;
        if (_holdFromRight)
            return;
        CancelFastHold();
        if (_fastFromHold)
        {
            EndFast(hold: true);
            Chrome.ReleaseMouseCapture();
            return;
        }

        if (pause)
            ArmClickPause();
    }

    private void ChromeRightDown(object sender, MouseButtonEventArgs e)
    {
        if (!_playerOpen)
            return;
        if (IsPlayerControl(e.OriginalSource as DependencyObject) || IsPlayerPanel(e.OriginalSource as DependencyObject) || IsInsideBar(e.OriginalSource as DependencyObject))
            return;
        if (PointerBand(e) != 0)
            return;
        ArmFastHold(true);
        e.Handled = true;
    }

    private void ChromeRightUp(object sender, MouseButtonEventArgs e)
    {
        if (!_holdFromRight)
            return;
        CancelFastHold();
        if (_fastFromHold)
            EndFast(hold: true);
        Chrome.ReleaseMouseCapture();
        e.Handled = true;
    }

    private int PointerBand(MouseEventArgs e)
    {
        var width = Chrome.ActualWidth;
        if (width <= 1)
            return 0;
        var across = e.GetPosition(Chrome).X / width;
        if (across >= 2d / 3d)
            return 1;
        if (across <= 1d / 3d)
            return -1;
        return 0;
    }

    private void ArmFastHold(bool right)
    {
        _fastHoldPending = true;
        _holdFromRight = right;
        _fastHoldTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
        _fastHoldTimer.Tick -= OnFastHold;
        _fastHoldTimer.Tick += OnFastHold;
        _fastHoldTimer.Stop();
        _fastHoldTimer.Start();
    }

    private void OnFastHold(object? sender, EventArgs e)
    {
        _fastHoldTimer?.Stop();
        var pressed = _holdFromRight ? Mouse.RightButton == MouseButtonState.Pressed : Mouse.LeftButton == MouseButtonState.Pressed;
        if (!_fastHoldPending || !_playerOpen || !pressed)
            return;
        _fastHoldPending = false;
        _pauseOnRelease = false;
        _clickPauseTimer?.Stop();
        Chrome.CaptureMouse();
        BeginFast(hold: true);
    }

    private bool _pauseOnRelease;
    private DispatcherTimer? _clickPauseTimer;

    private void ArmClickPause()
    {
        _clickPauseTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _clickPauseTimer.Tick -= OnClickPause;
        _clickPauseTimer.Tick += OnClickPause;
        _clickPauseTimer.Stop();
        _clickPauseTimer.Start();
    }

    private void OnClickPause(object? sender, EventArgs e)
    {
        _clickPauseTimer?.Stop();
        if (_playerOpen)
            TogglePause(this, new RoutedEventArgs());
    }

    private void ArmSpaceHold()
    {
        _spaceHoldPending = true;
        _spaceHoldTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _spaceHoldTimer.Tick -= OnSpaceHold;
        _spaceHoldTimer.Tick += OnSpaceHold;
        _spaceHoldTimer.Stop();
        _spaceHoldTimer.Start();
    }

    private void OnSpaceHold(object? sender, EventArgs e)
    {
        _spaceHoldTimer?.Stop();
        if (!_spaceHoldPending || !_playerOpen || !Keyboard.IsKeyDown(Key.Space))
            return;
        _spaceHoldPending = false;
        BeginFast(hold: false);
    }

    private void CancelFastHold()
    {
        _fastHoldPending = false;
        _fastHoldTimer?.Stop();
    }

    private void BeginFast(bool hold)
    {
        if (!_playerOpen)
            return;
        if (!_fastFromHold && !_fastFromSpace)
            _rateBeforeFast = App.Engine.Rate < 0.1f ? 1 : App.Engine.Rate;
        if (hold)
            _fastFromHold = true;
        else
            _fastFromSpace = true;
        App.Playback.SetRate(2);
    }

    private void EndFast(bool hold)
    {
        if (hold)
            _fastFromHold = false;
        else
            _fastFromSpace = false;
        if (_fastFromHold || _fastFromSpace || !_playerOpen)
            return;
        var restore = _rateBeforeFast < 0.1f ? 1 : _rateBeforeFast;
        App.Playback.SetRate(restore);
    }

    private void CancelFast()
    {
        CancelFastHold();
        _spaceHoldPending = false;
        _spaceHoldTimer?.Stop();
        var restore = _fastFromHold || _fastFromSpace;
        _fastFromHold = false;
        _fastFromSpace = false;
        if (restore && _playerOpen)
            App.Playback.SetRate(_rateBeforeFast < 0.1f ? 1 : _rateBeforeFast);
        Chrome.ReleaseMouseCapture();
    }

    private bool IsPlayerPanel(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source == MorePanel || source == QueuePanel || source == KeyGuide || source == FailurePanel || source == PlayerSourcePanel || source == PlayerReplacePanel || source == StatsPanel)
                return true;
            source = VisualTreeHelper.GetParent(source);
        }

        return false;
    }

    private static readonly SolidColorBrush OverlayClear = FreezeClear();

    private static SolidColorBrush FreezeClear()
    {
        var brush = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0));
        brush.Freeze();
        return brush;
    }

    private static void QuietOverlay()
    {
        foreach (System.Windows.Window window in System.Windows.Application.Current.Windows)
        {
            if (window.Title != "LibVLCSharp.WPF")
                continue;
            if (window.Background is SolidColorBrush solid && solid.Color.A == 1 && solid.Color.R == 0 && solid.Color.G == 0 && solid.Color.B == 0)
                continue;
            window.Background = OverlayClear;
        }
    }

    private void ChromeMouseMove(object sender, MouseEventArgs e)
    {
        var point = e.GetPosition(Chrome);
        if (_volumeArmed && Mouse.LeftButton == MouseButtonState.Pressed)
        {
            var dy = _volumeStart.Y - e.GetPosition(Chrome).Y;
            if (Math.Abs(dy) >= 8)
            {
                var height = Math.Max(Chrome.ActualHeight, 1);
                VolumeSlider.Value = Math.Clamp(_volumeOrigin + dy / height * 100d, 0, 100);
            }
        }

        var moved = point != _chromeCursor;
        _chromeCursor = point;
        if (!moved)
            return;
        if (!Chrome.IsKeyboardFocused && Keyboard.FocusedElement is not TextBox)
            Chrome.Focus();
        ShowChrome();
    }

    private void ChromeFocused(object sender, KeyboardFocusChangedEventArgs e) => ShowChrome();

    private void SeekChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_updatingSeek || !_playerOpen)
            return;
        if (_seekTarget is long pending && !SeekBar.IsMouseCaptureWithin && Math.Abs(e.NewValue - pending) > 1500)
            return;
        _pendingSeek = (long)e.NewValue;
        SeekHintText.Text = Format(_pendingSeek);
        SeekHint.Visibility = Visibility.Visible;
        _seekTimer.Stop();
        _seekTimer.Start();
    }

    private void VolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (IsLoaded)
            App.Engine.SetVolume((int)e.NewValue);
        if (!_rememberVolume)
            return;
        PlaybackLook.Current.Volume = (int)Math.Clamp(e.NewValue, 0, 100);
        _volumeSaveTimer.Stop();
        _volumeSaveTimer.Start();
    }

    private void OnSnapshot(object? sender, PlayerSnapshot snapshot)
    {
        if (!_playerOpen)
            return;
        if (_awaitingPicture && snapshot.PlaybackStarted)
        {
            _awaitingPicture = false;
            LoadingWash.Visibility = Visibility.Collapsed;
            BlackenVideo();
        }

        if (!_subtitleStyleLive && snapshot.PlaybackStarted && _subtitleStyleTries < 40)
        {
            _subtitleStyleTries++;
            _subtitleStyleLive = App.Engine.RefreshSubtitleStyle();
        }

        _durationMs = snapshot.DurationMs;
        if (_seekTarget is long target)
        {
            var away = Math.Abs(snapshot.PositionMs - target);
            var landed = away < 4_000 || (Math.Abs(snapshot.PositionMs - _seekOrigin) > away && away < 12_000);
            if (Environment.TickCount64 >= _seekUntil && landed)
            {
                _seekTarget = null;
                _seekTries = 0;
                SeekingMark.Visibility = Visibility.Collapsed;
            }
            else if (Environment.TickCount64 >= _seekUntil)
            {
                if (_seekTries < 1)
                {
                    _seekTries++;
                    _seekUntil = Environment.TickCount64 + 1600;
                    App.Playback.Seek(target, _durationMs);
                }
                else
                {
                    _seekTarget = null;
                    _seekTries = 0;
                    SeekingMark.Visibility = Visibility.Collapsed;
                }
            }
        }

        var chromeUp = PlayerBar.Opacity > 0;
        if (_seekTarget is null)
            _positionMs = snapshot.PositionMs;
        if (chromeUp)
        {
            var dragging = SeekBar.IsMouseCaptureWithin;
            _updatingSeek = true;
            SeekBar.Maximum = Math.Max(snapshot.DurationMs, 1);
            if (!dragging && _seekTarget is null)
                SeekBar.Value = Math.Clamp(snapshot.PositionMs, 0, SeekBar.Maximum);
            _updatingSeek = false;
            if (_seekTarget is null)
                TimeCurrent.Text = Format(snapshot.PositionMs);
            TimeRemaining.Text = "-" + Format(Math.Max(0, snapshot.DurationMs - (_seekTarget ?? snapshot.PositionMs)));
            PlayIcon.Visibility = snapshot.IsPaused ? Visibility.Visible : Visibility.Collapsed;
            PauseIcon.Visibility = snapshot.IsPaused ? Visibility.Collapsed : Visibility.Visible;
        }
        if (snapshot.IsPaused != _chromeHeld)
        {
            _chromeHeld = snapshot.IsPaused;
            if (_chromeHeld)
                ShowChrome();
            else if (!_reducedMotion)
            {
                _chromeTimer.Stop();
                _chromeTimer.Start();
            }
        }
        if (chromeUp && snapshot.BufferedMs is long buffered)
        {
            BufferBar.Visibility = Visibility.Visible;
            BufferBar.Maximum = SeekBar.Maximum;
            BufferBar.Value = buffered;
        }

        if (chromeUp)
        {
            SkipIntroButton.Visibility = _introEndMs is long end && snapshot.PositionMs < end
                ? Visibility.Visible
                : Visibility.Collapsed;
            PlaceSubtitles(true);
        }

        var inCredits = _creditsMs is long start && snapshot.PositionMs >= start;
        if (!_markedFinished && _creditsMs is long creditStart && snapshot.PositionMs >= creditStart && snapshot.DurationMs > creditStart)
        {
            _markedFinished = true;
            App.Playback.MarkFinished();
        }

        inCredits = inCredits
            && WatchQueue.Ids.Count > 1
            && _playing is not null
            && WatchQueue.Next(_playing.MediaId) is not null;
        NextEpisodeButton.Visibility = inCredits ? Visibility.Visible : Visibility.Collapsed;

        if (_coveredWidth == 0)
            FillVideo();
        RememberSubtitle(snapshot);
    }

    private void RememberSubtitle(PlayerSnapshot snapshot)
    {
        if (_subsChosen || snapshot.DurationMs <= 0)
            return;
        var tracks = App.Engine.SubtitleTracks.Where(track => track.Id >= 0).ToArray();
        if (tracks.Length == 0)
            return;
        _subsChosen = true;
        var look = PlaybackLook.Current;
        if (!look.SubtitlesOn)
            return;
        var match = tracks.FirstOrDefault(track =>
            look.Language.Length > 0 && track.Name.Contains(look.Language, StringComparison.OrdinalIgnoreCase))
            ?? tracks[0];
        App.Playback.SelectSubtitle(match.Id);
    }

    private void OnFault(object? sender, PlayerFault fault)
    {
        App.Playback.StopPersisting();
        if (_playing is null)
            return;
        _awaitingPicture = false;
        LoadingWash.Visibility = Visibility.Collapsed;
        FailureText.Text = PlaybackFailureCopy.Describe(
            _playing.IsLoopback, _playing.SuggestsStremioServer, _playing.MayExpire, fault.AuthenticationFailed);
        FailurePanel.Visibility = Visibility.Visible;
        ShowChrome();
    }

    private void PlayerKey(object sender, KeyEventArgs e) => OnPreviewKeyDown(e);

    private void PlayerKeyUp(object sender, KeyEventArgs e) => OnPreviewKeyUp(e);

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Handled || !_playerOpen || Keyboard.FocusedElement is TextBox)
            return;
        ShowChrome();
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        switch (key)
        {
            case Key.Space:
                if (!e.IsRepeat)
                    ArmSpaceHold();
                e.Handled = true;
                break;
            case Key.K:
                TogglePause(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Escape:
                if (KeyGuide.Visibility == Visibility.Visible)
                    KeyGuide.Visibility = Visibility.Collapsed;
                else if (QueuePanel.Visibility == Visibility.Visible)
                    QueuePanel.Visibility = Visibility.Collapsed;
                else if (MorePanel.Visibility == Visibility.Visible)
                {
                    MorePanel.Visibility = Visibility.Collapsed;
                    _openSection = "";
                }
                else if (_fullScreen)
                    ToggleFullScreen(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Left:
                Jump(-SkipStep());
                e.Handled = true;
                break;
            case Key.Right:
                Jump(SkipStep());
                e.Handled = true;
                break;
            case Key.Up:
                VolumeSlider.Value = Math.Min(100, VolumeSlider.Value + 5);
                e.Handled = true;
                break;
            case Key.Down:
                VolumeSlider.Value = Math.Max(0, VolumeSlider.Value - 5);
                e.Handled = true;
                break;
            case Key.F:
                ToggleFullScreen(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.M:
                ToggleMute();
                e.Handled = true;
                break;
            case Key.C:
                CycleSubtitles();
                e.Handled = true;
                break;
            case Key.Home:
                CommitSeek(0);
                e.Handled = true;
                break;
            case Key.Oem4:
                NudgeRate(-0.25f);
                e.Handled = true;
                break;
            case Key.Oem6:
                NudgeRate(0.25f);
                e.Handled = true;
                break;
            case Key.I:
                _statsOpen = !_statsOpen;
                StartStats();
                e.Handled = true;
                break;
            case Key.F1:
            case Key.Oem2 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                KeyGuide.Visibility = KeyGuide.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                e.Handled = true;
                break;
        }
    }

    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e);
        if (e.Handled || !_playerOpen || Keyboard.FocusedElement is TextBox)
            return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key != Key.Space)
            return;
        _spaceHoldTimer?.Stop();
        var pending = _spaceHoldPending;
        _spaceHoldPending = false;
        if (_fastFromSpace)
            EndFast(hold: false);
        else if (pending)
            TogglePause(this, new RoutedEventArgs());
        e.Handled = true;
    }

    private static long SkipStep()
    {
        var mods = Keyboard.Modifiers;
        var ctrl = mods.HasFlag(ModifierKeys.Control);
        var shift = mods.HasFlag(ModifierKeys.Shift);
        var alt = mods.HasFlag(ModifierKeys.Alt);
        if (ctrl && shift)
            return 60_000;
        if (alt)
            return 30_000;
        if (ctrl)
            return 15_000;
        if (shift)
            return 5_000;
        return 10_000;
    }

    private void ToggleMute()
    {
        if (VolumeSlider.Value > 0)
        {
            _volumeBeforeMute = VolumeSlider.Value;
            VolumeSlider.Value = 0;
            Flash("Muted");
            return;
        }

        VolumeSlider.Value = _volumeBeforeMute <= 0 ? 100 : _volumeBeforeMute;
        Flash("Volume " + ((int)VolumeSlider.Value).ToString(CultureInfo.InvariantCulture));
    }

    private void NudgeRate(float delta)
    {
        _fastFromHold = false;
        _fastFromSpace = false;
        var next = Math.Clamp(App.Engine.Rate + delta, 0.5f, 2f);
        _rateBeforeFast = next;
        App.Playback.SetRate(next);
        Flash(next.ToString("0.##", CultureInfo.InvariantCulture) + "×");
    }

    private void CycleSubtitles()
    {
        var tracks = App.Engine.SubtitleTracks.Where(track => track.Id >= 0).ToArray();
        if (tracks.Length == 0)
        {
            Flash("No subtitles");
            return;
        }

        var look = PlaybackLook.Current;
        if (!look.SubtitlesOn)
        {
            var match = tracks.FirstOrDefault(track =>
                look.Language.Length > 0 && track.Name.Contains(look.Language, StringComparison.OrdinalIgnoreCase))
                ?? tracks[0];
            ChooseSubtitle(match.Id, match.Name);
            Flash(match.Name);
            return;
        }

        var index = Array.FindIndex(tracks, track => track.Name == look.Language);
        if (index < 0 || index + 1 >= tracks.Length)
        {
            ChooseSubtitle(-1, look.Language);
            Flash("Subtitles off");
            return;
        }

        var next = tracks[index + 1];
        ChooseSubtitle(next.Id, next.Name);
        Flash(next.Name);
    }

    private void ChooseSubtitle(int id, string language)
    {
        var look = PlaybackLook.Current;
        look.SubtitlesOn = id >= 0;
        if (id >= 0)
            look.Language = language;
        look.Save();
        App.Playback.SelectSubtitle(id);
    }

    internal void FillKeyGuide()
    {
        if (KeyList.Children.Count > 0)
            return;
        (string Label, string[] Keys)[] rows =
        [
            ("Play / Pause", ["Space"]),
            ("Play / Pause", ["K"]),
            ("Fast forward 2×", ["Hold", "Space"]),
            ("Fast forward 2×", ["Hold middle"]),
            ("Back 10 seconds", ["←"]),
            ("Forward 10 seconds", ["→"]),
            ("Back 5 seconds", ["Shift", "←"]),
            ("Forward 5 seconds", ["Shift", "→"]),
            ("Back 15 seconds", ["Ctrl", "←"]),
            ("Forward 15 seconds", ["Ctrl", "→"]),
            ("Back 30 seconds", ["Alt", "←"]),
            ("Forward 30 seconds", ["Alt", "→"]),
            ("Back 1 minute", ["Ctrl", "Shift", "←"]),
            ("Forward 1 minute", ["Ctrl", "Shift", "→"]),
            ("Volume up", ["↑"]),
            ("Volume down", ["↓"]),
            ("Mute", ["M"]),
            ("Full screen", ["F"]),
            ("Full screen", ["Double-click"]),
            ("Leave full screen", ["Esc"]),
            ("Subtitles", ["C"]),
            ("Slower", ["["]),
            ("Faster", ["]"]),
            ("Start", ["Home"]),
            ("Stats", ["I"]),
            ("This guide", ["?"]),
        ];
        foreach (var (label, keys) in rows)
            KeyList.Children.Add(KeyRow(label, keys));
    }

    private static UIElement KeyRow(string label, string[] keys)
    {
        var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var name = new TextBlock
        {
            Text = label,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 13
        };
        var chips = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < keys.Length; i++)
        {
            if (i > 0)
            {
                chips.Children.Add(new TextBlock
                {
                    Text = "+",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 2, 0),
                    FontSize = 12
                });
            }

            chips.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = keys[i],
                    Foreground = Brushes.White,
                    FontSize = 12
                }
            });
        }

        Grid.SetColumn(chips, 1);
        row.Children.Add(name);
        row.Children.Add(chips);
        return row;
    }

    private void OnEngineLoading(object? sender, bool loading)
    {
        if (!_playerOpen || _seekTarget is null || !loading)
            return;
        SeekingMark.Visibility = Visibility.Visible;
    }

    private void ShowChrome()
    {
        if (!_playerOpen)
            return;
        Chrome.Opacity = 1;
        PlayerHeader.Opacity = 1;
        PlayerBack.Opacity = 1;
        PlayerBar.Opacity = 1;
        QueueActions.Opacity = 1;
        PlayerHeader.IsHitTestVisible = true;
        PlayerBack.IsHitTestVisible = true;
        PlayerBar.IsHitTestVisible = true;
        QueueActions.IsHitTestVisible = true;
        if (_seekTarget is null)
        {
            _updatingSeek = true;
            SeekBar.Maximum = Math.Max(_durationMs, 1);
            SeekBar.Value = Math.Clamp(_positionMs, 0, SeekBar.Maximum);
            _updatingSeek = false;
            TimeCurrent.Text = Format(_positionMs);
            TimeRemaining.Text = "-" + Format(Math.Max(0, _durationMs - _positionMs));
        }

        var paused = App.Engine.Current.IsPaused;
        PlayIcon.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
        PauseIcon.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
        StartGlass();
        _chromeTimer.Stop();
        PlaceSubtitles(true);
        SyncPlayCursor();
        if (_reducedMotion || !_playerOpen || _chromeHeld)
            return;
        _chromeTimer.Start();
    }

    private void HideChrome()
    {
        if (MorePanel.Visibility == Visibility.Visible
            || PlayerSourcePanel.Visibility == Visibility.Visible
            || PlayerReplacePanel.Visibility == Visibility.Visible
            || FailurePanel.Visibility == Visibility.Visible)
            return;
        PlayerHeader.Opacity = 0;
        PlayerBack.Opacity = 0;
        PlayerBar.Opacity = 0;
        QueueActions.Opacity = 0;
        PlayerHeader.IsHitTestVisible = false;
        PlayerBack.IsHitTestVisible = false;
        PlayerBar.IsHitTestVisible = false;
        QueueActions.IsHitTestVisible = false;
        SeekHint.Visibility = Visibility.Collapsed;
        PlayerBlur.Background = Brushes.Transparent;
        PlaceSubtitles(false);
        SyncPlayCursor();
    }

    private void SyncPlayCursor()
    {
        Mouse.OverrideCursor = _playerOpen && _fullScreen && PlayerBar.Opacity == 0 ? Cursors.None : null;
    }

    private bool _subtitleStyleLive;
    private int _subtitleStyleTries;
    private DispatcherTimer? _subtitleSlide;
    private long _subtitleSlideStart;
    private int _subtitleFrom;
    private int _subtitleTo = -1;

    private void PlaceSubtitles(bool aboveBar)
    {
        if (!_playerOpen)
            return;
        var target = SubtitleMargin(aboveBar);
        if (_compact || _subtitleMargin < 0 || _reducedMotion)
        {
            _subtitleSlide?.Stop();
            _subtitleTo = target;
            ApplySubtitleMargin(target);
            return;
        }

        if (target == _subtitleTo && (_subtitleSlide?.IsEnabled == true || target == _subtitleMargin))
            return;
        _subtitleFrom = _subtitleMargin;
        _subtitleTo = target;
        _subtitleSlideStart = Environment.TickCount64;
        _subtitleSlide ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _subtitleSlide.Tick -= SlideSubtitles;
        _subtitleSlide.Tick += SlideSubtitles;
        _subtitleSlide.Start();
    }

    private void SlideSubtitles(object? sender, EventArgs e)
    {
        const int durationMs = 140;
        var t = (Environment.TickCount64 - _subtitleSlideStart) / (double)durationMs;
        if (t >= 1 || !_playerOpen)
        {
            _subtitleSlide?.Stop();
            if (_playerOpen)
                ApplySubtitleMargin(_subtitleTo);
            return;
        }

        var eased = 1 - Math.Pow(1 - t, 3);
        ApplySubtitleMargin((int)Math.Round(_subtitleFrom + (_subtitleTo - _subtitleFrom) * eased));
    }

    private void ApplySubtitleMargin(int margin)
    {
        if (margin == _subtitleMargin)
            return;
        if (!App.Engine.SetSubtitleMargin(margin))
            return;
        _subtitleMargin = margin;
    }

    private int SubtitleMargin(bool aboveBar) => aboveBar ? RaisedMargin() : LoweredMargin();

    private int LoweredMargin()
    {
        var dpi = VisualTreeHelper.GetDpi(PlayerBar).PixelsPerDip;
        if (dpi <= 0)
            dpi = 1;
        return (int)Math.Round(-48 * dpi);
    }

    private int _pictureW;
    private int _pictureH;

    private int RaisedMargin()
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var scaleY = dpi.DpiScaleY <= 0 ? 1 : dpi.DpiScaleY;
        var barDip = PlayerBar.ActualHeight + PlayerBar.Margin.Bottom;
        if (barDip < 40)
            barDip = 120;
        var pixels = (barDip + 18) * scaleY;
        if (!_compact || !TryPicturePixels(out var srcW, out var srcH) || Video.ActualHeight < 2)
            return (int)Math.Round(pixels);

        var scaleX = dpi.DpiScaleX <= 0 ? 1 : dpi.DpiScaleX;
        var hostW = Math.Max(1, Video.ActualWidth * scaleX);
        var hostH = Math.Max(1, Video.ActualHeight * scaleY);
        var fit = Math.Min(hostW / srcW, hostH / srcH);
        if (fit < 0.05)
            return (int)Math.Round(pixels);
        var letterbox = Math.Max(0, (hostH - srcH * fit) / 2);
        var overlap = Math.Max(32 * scaleY, barDip * scaleY - letterbox + 16 * scaleY);
        return (int)Math.Round(overlap / fit);
    }

    private bool TryPicturePixels(out int width, out int height)
    {
        if (_pictureW > 0 && _pictureH > 0)
        {
            width = _pictureW;
            height = _pictureH;
            return true;
        }

        width = 0;
        height = 0;
        LibVLCSharp.Shared.Media? media = null;
        try
        {
            media = App.Engine.Player.Media;
            var tracks = media?.Tracks;
            if (tracks is null)
                return false;
            foreach (var track in tracks)
            {
                if (track.TrackType != LibVLCSharp.Shared.TrackType.Video)
                    continue;
                var frame = track.Data.Video;
                if (frame.Width == 0 || frame.Height == 0)
                    continue;
                var sar = frame.SarDen == 0 ? 1d : frame.SarNum / (double)frame.SarDen;
                width = Math.Max(1, (int)Math.Round(frame.Width * (sar <= 0 ? 1 : sar)));
                height = Math.Max(1, (int)frame.Height);
                _pictureW = width;
                _pictureH = height;
                return true;
            }
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            media?.Dispose();
        }

        return false;
    }

    private bool _statsOpen;
    private DispatcherTimer? _statsTimer;
    private long _statsStamp;
    private int _statsDisplayed;
    private int _statsRead;
    private bool _statsReady;
    private string _statsVideoCodec = "";
    private string _statsAudioCodec = "";
    private string _statsSourceSize = "";
    private float _statsSource;

    private void ToggleStats(object sender, RoutedEventArgs e)
    {
        _statsOpen = !_statsOpen;
        StartStats();
    }

    private void CloseStats(object sender, RoutedEventArgs e)
    {
        _statsOpen = false;
        StartStats();
    }

    private void StartStats()
    {
        _statsStamp = 0;
        _statsDisplayed = 0;
        _statsRead = 0;
        _statsReady = false;
        _statsVideoCodec = "";
        _statsAudioCodec = "";
        _statsSourceSize = "";
        _statsSource = 0;
        StatsPanel.Visibility = _statsOpen ? Visibility.Visible : Visibility.Collapsed;
        if (!_statsOpen || !_playerOpen)
        {
            _statsTimer?.Stop();
            return;
        }

        _statsTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _statsTimer.Tick -= OnStatsTick;
        _statsTimer.Tick += OnStatsTick;
        _statsTimer.Start();
        SampleStats();
    }

    private void OnStatsTick(object? sender, EventArgs e)
    {
        if (!_playerOpen || !_statsOpen)
        {
            _statsTimer?.Stop();
            return;
        }

        SampleStats();
    }

    private void SampleStats()
    {
        LibVLCSharp.Shared.Media? media = null;
        try
        {
            var player = App.Engine.Player;
            media = player.Media;
            if (media is null)
            {
                StatsViewport.Text = "Waiting";
                return;
            }

            var stats = media.Statistics;
            var now = Stopwatch.GetTimestamp();
            var shown = 0d;
            var seconds = 0d;
            if (_statsReady)
            {
                seconds = (now - _statsStamp) / (double)Stopwatch.Frequency;
                if (seconds > 0.05)
                    shown = Math.Max(0, stats.DisplayedPictures - _statsDisplayed) / seconds;
            }

            var read = stats.ReadBytes;
            var network = "—";
            if (_statsReady && seconds > 0.05)
            {
                var kb = Math.Max(0, read - _statsRead) / 1024d / seconds;
                network = kb.ToString("0", CultureInfo.InvariantCulture) + " KB/s";
            }

            _statsStamp = now;
            _statsDisplayed = stats.DisplayedPictures;
            _statsRead = read;
            if (_statsSourceSize.Length == 0)
                ReadPicture(media, player);

            var viewW = Math.Max(0, (int)Math.Round(Video.ActualWidth)).ToString(CultureInfo.InvariantCulture);
            var viewH = Math.Max(0, (int)Math.Round(Video.ActualHeight)).ToString(CultureInfo.InvariantCulture);
            var viewport = viewW + "x" + viewH;
            var fps = _statsReady && player.IsPlaying ? "@" + FormatFps(shown) : "";
            var optimal = _statsSourceSize.Length == 0
                ? "—"
                : _statsSourceSize + (_statsSource > 0 ? "@" + FormatFps(_statsSource) : "");
            var codecs = _statsVideoCodec.Length == 0 ? "—" : _statsVideoCodec;
            if (_statsAudioCodec.Length > 0)
                codecs += " / " + _statsAudioCodec;
            var kbps = stats.InputBitrate * 8 / 1000d;
            var volume = player.Volume < 0 ? 0 : player.Volume;
            var health = player.State == LibVLCSharp.Shared.VLCState.Buffering
                ? "Buffering"
                : player.IsPlaying ? "Playing" : "Paused";
            if (stats.DemuxDiscontinuity > 0)
                health += " · " + stats.DemuxDiscontinuity.ToString(CultureInfo.InvariantCulture) + " stalls";

            StatsViewport.Text = viewport + " / " + stats.LostPictures.ToString(CultureInfo.InvariantCulture)
                + " dropped of " + stats.DisplayedPictures.ToString(CultureInfo.InvariantCulture);
            StatsResolution.Text = viewport + fps + " / " + optimal;
            StatsVolume.Text = volume.ToString(CultureInfo.InvariantCulture) + "%";
            StatsCodecs.Text = codecs;
            StatsSpeed.Text = kbps >= 1 ? Math.Round(kbps).ToString("0", CultureInfo.InvariantCulture) + " Kbps" : "—";
            StatsNetwork.Text = network;
            StatsBuffer.Text = health;
            _statsReady = true;
        }
        catch (Exception)
        {
        }
        finally
        {
            media?.Dispose();
        }
    }

    private void ReadPicture(LibVLCSharp.Shared.Media media, LibVLCSharp.Shared.MediaPlayer player)
    {
        _statsSource = player.Fps;
        var tracks = media.Tracks;
        if (tracks is null)
            return;
        foreach (var track in tracks)
        {
            if (track.TrackType == LibVLCSharp.Shared.TrackType.Video && _statsSourceSize.Length == 0 && track.Data.Video.Width > 0)
            {
                var frame = track.Data.Video;
                var sar = frame.SarDen == 0 ? 1d : frame.SarNum / (double)frame.SarDen;
                var width = (int)Math.Round(frame.Width * (sar <= 0 ? 1 : sar));
                _statsSourceSize = width.ToString(CultureInfo.InvariantCulture) + "x" + frame.Height.ToString(CultureInfo.InvariantCulture);
                _statsVideoCodec = FourCC(track.Codec);
                if (frame.FrameRateDen > 0)
                    _statsSource = frame.FrameRateNum / (float)frame.FrameRateDen;
            }
            else if (track.TrackType == LibVLCSharp.Shared.TrackType.Audio && _statsAudioCodec.Length == 0)
            {
                _statsAudioCodec = FourCC(track.Codec);
            }
        }
    }

    private static string FormatFps(double value) =>
        Math.Abs(value - Math.Round(value)) < 0.05
            ? Math.Round(value).ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.0", CultureInfo.InvariantCulture);

    private static string FourCC(uint code)
    {
        var chars = new char[4];
        var n = 0;
        for (var i = 0; i < 4; i++)
        {
            var c = (char)((code >> (8 * i)) & 0xff);
            if (c is < (char)33 or > (char)126)
                continue;
            chars[n++] = char.ToUpperInvariant(c);
        }

        return n == 0 ? "" : new string(chars, 0, n);
    }

    private void Flash(string label)
    {
        CenterFeedback.Text = label;
        CenterFeedback.Visibility = Visibility.Visible;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private void RememberTrakt(bool paused)
    {
        if (_playing is null || _playing.MediaId == Guid.Empty)
            return;
        if (!TraktQueue.CanSend(App.ReadSecret("secret.trakt")))
            return;
        App.Library.Store.EnqueueSync(
            App.Library.Store.ActiveProfileId, _playing.MediaId, paused ? "pause" : "stop",
            TraktQueue.ScrobbleBody(_playing.Title, _positionMs, _durationMs, paused));
    }

    private static long ResumeStart(PlayTarget target)
    {
        var decision = App.Playback.Decide(target);
        return decision.Kind is ResumeKind.ResumeQuiet or ResumeKind.OfferResume ? decision.PositionMs : 0;
    }

    private static Guid? PreviousEpisode(PlayTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.ShowTitle) || target.EpisodeNumber is null)
            return null;
        return App.Library.Store.Episodes(target.ShowTitle)
            .Where(item => item.MediaId != target.MediaId)
            .OrderByDescending(item => item.SeasonNumber ?? 0)
            .ThenByDescending(item => item.EpisodeNumber ?? 0)
            .FirstOrDefault(item =>
                (item.SeasonNumber ?? 0) < (target.SeasonNumber ?? 0)
                || ((item.SeasonNumber ?? 0) == (target.SeasonNumber ?? 0) && (item.EpisodeNumber ?? 0) < target.EpisodeNumber))
            ?.MediaId;
    }

    private static Guid? NextEpisode(PlayTarget target)
    {
        if (string.IsNullOrWhiteSpace(target.ShowTitle) || target.EpisodeNumber is null)
            return null;
        return App.Library.Store.Episodes(target.ShowTitle)
            .Where(item => item.MediaId != target.MediaId)
            .OrderBy(item => item.SeasonNumber ?? 0)
            .ThenBy(item => item.EpisodeNumber ?? 0)
            .FirstOrDefault(item =>
                (item.SeasonNumber ?? 0) > (target.SeasonNumber ?? 0)
                || ((item.SeasonNumber ?? 0) == (target.SeasonNumber ?? 0) && (item.EpisodeNumber ?? 0) > target.EpisodeNumber))
            ?.MediaId;
    }

    private static string Format(long ms)
    {
        var time = TimeSpan.FromMilliseconds(Math.Max(0, ms));
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

}
