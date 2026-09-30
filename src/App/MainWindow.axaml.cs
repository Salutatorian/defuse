using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Defuse.Application;
using Defuse.Shell;

namespace Defuse.Cross;

public partial class MainWindow : Window
{
    private readonly PlayerSession _session = App.Host.Session;
    private readonly DispatcherTimer _seekTimer;
    private readonly string _volumePath;
    private bool _updatingSeek;
    private bool _draggingSeek;
    private long _pendingSeek;
    private long _seekTarget = -1;
    private long _seekOrigin;
    private long _seekUntil;
    private int _seekTries;
    private long _duration;
    private bool _paused;
    private bool _audioMenu;
    private Guid? _playing;

    public MainWindow()
    {
        InitializeComponent();
        _volumePath = Path.Combine(_session.Root, "playback.json");
        Volume.Value = ReadVolume();
        Volume.PropertyChanged += (_, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                _session.Engine.SetVolume((int)Volume.Value);
        };
        _session.Engine.SetVolume((int)Volume.Value);
        _session.Engine.SnapshotChanged += OnSnapshot;
        _session.Engine.Faulted += (_, fault) => Status.Text = fault.SafeMessage;
        SeekBar.AddHandler(PointerPressedEvent, OnSeekPressed, RoutingStrategies.Tunnel);
        _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _seekTimer.Tick += (_, _) => FinishSeek();
        RefreshHome();
    }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        try
        {
            var ids = _session.SaveLinks(AddUrl.Text ?? "");
            if (ids.Count == 0)
            {
                Status.Text = "Paste a link first.";
                return;
            }

            AddUrl.Text = "";
            RefreshHome();
            OpenPlayer(ids[0]);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
    }

    private void OnPlayOnce(object? sender, RoutedEventArgs e)
    {
        var line = FirstLine(AddUrl.Text);
        if (line.Length == 0)
        {
            Status.Text = "Paste a link first.";
            return;
        }

        try
        {
            _playing = null;
            _session.PlayOnce(line);
            ShowPlayer(line);
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
    }

    private void OpenPlayer(Guid id)
    {
        _playing = id;
        _session.Play(id);
        var title = _session.Library.Library().FirstOrDefault(item => item.MediaId == id)?.Title ?? "Playing";
        ShowPlayer(LibraryController.DisplayTitle(title));
    }

    private void ShowPlayer(string title)
    {
        PlayingTitle.Text = title;
        HomePage.IsVisible = false;
        PlayerPage.IsVisible = true;
        Video.MediaPlayer = _session.Engine.Player;
        NextButton.IsVisible = _session.Next() is not null;
        TrackMenu.IsVisible = false;
        _seekTarget = -1;
    }

    private void OnLeave(object? sender, RoutedEventArgs e)
    {
        _session.Stop();
        PlayerPage.IsVisible = false;
        HomePage.IsVisible = true;
        RefreshHome();
    }

    private void OnPause(object? sender, RoutedEventArgs e) => TogglePause();

    private void OnBack10(object? sender, RoutedEventArgs e) => Jump(-10_000);

    private void OnForward10(object? sender, RoutedEventArgs e) => Jump(10_000);

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_session.Next() is Guid id)
            OpenPlayer(id);
    }

    private async void OnReplace(object? sender, RoutedEventArgs e)
    {
        if (_playing is not Guid id)
        {
            Status.Text = "Save the title before replacing its link.";
            return;
        }

        var box = new TextBox { Watermark = "New link", MinWidth = 420 };
        var ok = new Button { Content = "Replace", Margin = new Avalonia.Thickness(8, 0, 0, 0) };
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8, Margin = new Avalonia.Thickness(16) };
        row.Children.Add(box);
        row.Children.Add(ok);
        var dialog = new Window
        {
            Title = "Replace link",
            Width = 560,
            Height = 120,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brushes.Black,
            Content = row
        };
        ok.Click += (_, _) => dialog.Close(box.Text);
        var raw = await dialog.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(raw))
            return;
        try
        {
            _session.Replace(id, raw.Trim());
            ShowPlayer(PlayingTitle.Text ?? "Playing");
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
    }

    private void OnSpeed(object? sender, RoutedEventArgs e)
    {
        var next = _session.Playback.Rate switch
        {
            < 1.1f => 1.25f,
            < 1.4f => 1.5f,
            < 1.8f => 2f,
            _ => 1f
        };
        _session.Playback.SetRate(next);
        SpeedButton.Content = next.ToString("0.##") + "×";
    }

    private void OnAudio(object? sender, RoutedEventArgs e) => ShowTracks(audio: true);

    private void OnSubs(object? sender, RoutedEventArgs e) => ShowTracks(audio: false);

    private void ShowTracks(bool audio)
    {
        _audioMenu = audio;
        var tracks = audio ? _session.Playback.AudioTracks : _session.Playback.SubtitleTracks;
        TrackList.ItemsSource = tracks.Select(track => new TrackRow(track.Id, track.Name)).ToArray();
        TrackMenu.IsVisible = true;
    }

    private void OnTrackPicked(object? sender, SelectionChangedEventArgs e)
    {
        if (TrackList.SelectedItem is not TrackRow row)
            return;
        if (_audioMenu)
            _session.Playback.SelectAudio(row.Id);
        else
            _session.Playback.SelectSubtitle(row.Id);
        TrackMenu.IsVisible = false;
    }

    private void OnPicture(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Button or Slider or ListBox or ListBoxItem)
            return;
        var point = e.GetPosition(Chrome);
        if (point.Y > Chrome.Bounds.Height - 140)
            return;
        var third = Chrome.Bounds.Width / 3;
        if (point.X > third && point.X < third * 2)
            TogglePause();
    }

    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        if (!PlayerPage.IsVisible || e.Handled)
            return;
        if (e.Key == Key.Space)
        {
            e.Handled = true;
            TogglePause();
        }
    }

    private async void OnAddKey(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.V || !e.KeyModifiers.HasFlag(KeyModifiers.Control))
            return;
        var top = TopLevel.GetTopLevel(this);
        if (top?.Clipboard is null)
            return;
        e.Handled = true;
#pragma warning disable CS0618
        var clip = await top.Clipboard.GetTextAsync() ?? "";
#pragma warning restore CS0618
        clip = clip.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (clip.Length == 0)
            return;
        var current = AddUrl.Text ?? "";
        var atLineStart = current.Length == 0 || current.EndsWith('\n') || current.EndsWith('\r');
        AddUrl.Text = current + (atLineStart ? "" : "\n") + clip + "\n";
        AddUrl.CaretIndex = AddUrl.Text.Length;
    }

    private void OnSeekPressed(object? sender, PointerPressedEventArgs e) => _draggingSeek = true;

    private void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        _draggingSeek = false;
        _pendingSeek = (long)(_duration * (SeekBar.Maximum <= 0 ? 0 : SeekBar.Value / SeekBar.Maximum));
        _seekTimer.Stop();
        _seekTimer.Start();
    }

    private void FinishSeek()
    {
        _seekTimer.Stop();
        CommitSeek(_pendingSeek);
    }

    private void Jump(long delta)
    {
        var current = _seekTarget >= 0 ? _seekTarget : (long)(_duration * SeekBar.Value / Math.Max(1, SeekBar.Maximum));
        CommitSeek(Math.Clamp(current + delta, 0, Math.Max(0, _duration)));
    }

    private void CommitSeek(long position)
    {
        _seekOrigin = (long)(_duration * SeekBar.Value / Math.Max(1, SeekBar.Maximum));
        _seekTarget = position;
        _seekTries = 0;
        _seekUntil = Environment.TickCount64 + 1600;
        _updatingSeek = true;
        SeekBar.Value = _duration <= 0 ? 0 : position / (double)_duration * SeekBar.Maximum;
        _updatingSeek = false;
        _session.Playback.Seek(position, _duration);
    }

    private void OnSnapshot(object? sender, PlayerSnapshot snapshot)
    {
        _duration = snapshot.DurationMs;
        _paused = snapshot.IsPaused;
        PauseButton.Content = _paused ? "Play" : "Pause";
        Clock.Text = Format(snapshot.PositionMs) + " / " + Format(snapshot.DurationMs);
        if (_draggingSeek || _updatingSeek)
            return;
        if (_seekTarget >= 0)
        {
            var landed = Environment.TickCount64 >= _seekUntil
                && (Math.Abs(snapshot.PositionMs - _seekTarget) < 4000
                    || (Math.Abs(snapshot.PositionMs - _seekTarget) < Math.Abs(_seekOrigin - _seekTarget) && Math.Abs(snapshot.PositionMs - _seekTarget) < 12000));
            if (!landed)
            {
                if (Environment.TickCount64 >= _seekUntil && _seekTries < 1)
                {
                    _seekTries++;
                    _seekUntil = Environment.TickCount64 + 1600;
                    _session.Playback.Seek(_seekTarget, snapshot.DurationMs);
                }

                return;
            }

            _seekTarget = -1;
        }

        _updatingSeek = true;
        SeekBar.Maximum = Math.Max(1, snapshot.DurationMs);
        SeekBar.Value = snapshot.PositionMs;
        _updatingSeek = false;
    }

    private void TogglePause()
    {
        if (_paused)
            _session.Playback.ResumePlayback();
        else
            _session.Playback.Pause();
    }

    private void RefreshHome()
    {
        ContinueList.ItemsSource = _session.Library.Continue()
            .Select(item => Tile(item.MediaId, LibraryController.DisplayTitle(item.Title)))
            .ToArray();
        var library = _session.Library.Library();
        LibraryList.ItemsSource = library
            .Where(item => !item.Completed)
            .Select(item => Tile(item.MediaId, LibraryController.DisplayTitle(item.Title)))
            .ToArray();
        DoneList.ItemsSource = library
            .Where(item => item.Completed)
            .Select(item => Tile(item.MediaId, LibraryController.DisplayTitle(item.Title)))
            .ToArray();
    }

    private Button Tile(Guid id, string title)
    {
        var button = new Button
        {
            Content = title,
            Tag = id,
            Width = 180,
            Height = 96,
            Margin = new Avalonia.Thickness(0, 0, 10, 10),
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left,
            VerticalContentAlignment = Avalonia.Layout.VerticalAlignment.Bottom
        };
        button.Click += (_, _) =>
        {
            try
            {
                OpenPlayer(id);
            }
            catch (Exception ex)
            {
                Status.Text = ex.Message;
            }
        };
        return button;
    }

    private int ReadVolume()
    {
        try
        {
            if (!File.Exists(_volumePath))
                return 80;
            var doc = JsonDocument.Parse(File.ReadAllText(_volumePath));
            return doc.RootElement.TryGetProperty("volume", out var value) ? value.GetInt32() : 80;
        }
        catch (Exception)
        {
            return 80;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        File.WriteAllText(_volumePath, JsonSerializer.Serialize(new { volume = (int)Volume.Value }));
        base.OnClosed(e);
    }

    private static string FirstLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";
        return text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "";
    }

    private static string Format(long ms)
    {
        if (ms < 0)
            ms = 0;
        var time = TimeSpan.FromMilliseconds(ms);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"m\:ss");
    }

    private sealed record TrackRow(int Id, string Name)
    {
        public override string ToString() => Name;
    }
}
