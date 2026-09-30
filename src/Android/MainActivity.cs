using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Defuse.Application;
using Defuse.Shell;
using VideoView = LibVLCSharp.Platforms.Android.VideoView;

namespace Defuse.Phone;

[Activity(Label = "Defuse", MainLauncher = true, Theme = "@android:style/Theme.Material.NoActionBar", ConfigurationChanges = Android.Content.PM.ConfigChanges.Orientation | Android.Content.PM.ConfigChanges.ScreenSize)]
public class MainActivity : Activity
{
    private PlayerSession? _session;
    private VideoView? _video;
    private LinearLayout? _home;
    private FrameLayout? _player;
    private TextView? _status;
    private EditText? _links;
    private TextView? _title;
    private TextView? _clock;
    private SeekBar? _seek;
    private SeekBar? _volume;
    private Button? _pause;
    private Button? _next;
    private LinearLayout? _continue;
    private LinearLayout? _library;
    private LinearLayout? _done;
    private long _duration;
    private bool _updatingSeek;
    private bool _dragging;
    private long _seekTarget = -1;
    private long _seekOrigin;
    private long _seekUntil;
    private int _seekTries;
    private bool _paused;
    private Guid? _playing;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var root = System.IO.Path.Combine(FilesDir!.AbsolutePath, "Defuse");
        _session = PlayerSession.Open(new AndroidUi(this), root, null);
        _session.Engine.SnapshotChanged += OnSnapshot;
        _session.Engine.Faulted += (_, fault) => RunOnUiThread(() => { if (_status is not null) _status.Text = fault.SafeMessage; });
        SetContentView(BuildUi());
        _session.Engine.SetVolume(_volume?.Progress ?? 80);
        Refresh();
        _ = Task.Run(StageUpdate);
    }

    private void StageUpdate()
    {
        var package = ReleaseUpdate.TryStage();
        if (package is null)
            return;
        RunOnUiThread(() => InstallPackage(package));
    }

    private void InstallPackage(string apkPath)
    {
        var installer = PackageManager?.PackageInstaller;
        if (installer is null)
            return;
        PackageInstaller.Session? session = null;
        try
        {
            var parameters = new PackageInstaller.SessionParams(PackageInstallMode.FullInstall);
            parameters.SetAppPackageName("com.defuse.player");
            var sessionId = installer.CreateSession(parameters);
            session = installer.OpenSession(sessionId);
            using (var input = System.IO.File.OpenRead(apkPath))
            using (var output = session.OpenWrite("base.apk", 0, input.Length))
            {
                input.CopyTo(output);
                session.Fsync(output);
            }

            var flags = PendingIntentFlags.UpdateCurrent;
            if (OperatingSystem.IsAndroidVersionAtLeast(31))
                flags |= PendingIntentFlags.Mutable;
            var pending = PendingIntent.GetActivity(this, sessionId, new Intent(this, Class), flags);
            if (pending?.IntentSender is null)
                return;
            session.Commit(pending.IntentSender);
        }
        catch (Exception ex) when (ex is Java.IO.IOException or Java.Lang.SecurityException or UnauthorizedAccessException)
        {
        }
        finally
        {
            session?.Close();
        }
    }

    protected override void OnDestroy()
    {
        _session?.Dispose();
        base.OnDestroy();
    }

    public override bool OnKeyDown(Keycode keyCode, KeyEvent? e)
    {
        if (keyCode == Keycode.Space && _player?.Visibility == ViewStates.Visible)
        {
            TogglePause();
            return true;
        }

        return base.OnKeyDown(keyCode, e);
    }

    private View BuildUi()
    {
        var root = new FrameLayout(this);
        _home = new LinearLayout(this) { Orientation = Orientation.Vertical };
        _home.SetPadding(32, 32, 32, 32);
        _home.SetBackgroundColor(Color.ParseColor("#171717"));
        var scroll = new ScrollView(this);
        var column = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(Label("Defuse", 28));
        column.AddView(Label("Paste one link per line.", 14));
        _links = new EditText(this) { Hint = "https://…" };
        _links.SetSingleLine(false);
        _links.SetMinLines(4);
        _links.SetTextColor(Color.ParseColor("#EBEBEB"));
        column.AddView(_links);
        var actions = Row();
        actions.AddView(Button("Save", Save));
        actions.AddView(Button("Play once", PlayOnce));
        column.AddView(actions);
        _status = Label("", 13);
        column.AddView(_status);
        column.AddView(Label("Continue", 18));
        _continue = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(_continue);
        column.AddView(Label("Library", 18));
        _library = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(_library);
        column.AddView(Label("Done watching", 18));
        _done = new LinearLayout(this) { Orientation = Orientation.Vertical };
        column.AddView(_done);
        scroll.AddView(column);
        _home.AddView(scroll);
        root.AddView(_home);

        _player = new FrameLayout(this) { Visibility = ViewStates.Gone };
        _player.SetBackgroundColor(Color.Black);
        _video = new VideoView(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
        _video.MediaPlayer = _session!.Engine.Player;
        _player.AddView(_video);
        var chrome = new FrameLayout(this) { LayoutParameters = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent) };
        var top = Row();
        top.SetPadding(24, 24, 24, 24);
        var back = Button("Back", Leave);
        _title = Label("Playing", 16);
        top.AddView(back);
        top.AddView(_title);
        chrome.AddView(top, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Top));
        _next = Button("Next", PlayNext);
        _next.Visibility = ViewStates.Gone;
        chrome.AddView(_next, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.WrapContent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Right | GravityFlags.Bottom) { BottomMargin = 280, RightMargin = 24 });
        var bar = new LinearLayout(this) { Orientation = Orientation.Vertical };
        bar.SetPadding(24, 16, 24, 24);
        bar.SetBackgroundColor(Color.ParseColor("#4A101010"));
        _seek = new SeekBar(this) { Max = 1000 };
        _seek.StartTrackingTouch += (_, _) => _dragging = true;
        _seek.StopTrackingTouch += (_, _) =>
        {
            _dragging = false;
            CommitSeek(_duration * _seek.Progress / Math.Max(1, _seek.Max));
        };
        bar.AddView(_seek);
        var controls = Row();
        controls.AddView(Button("−10", () => Jump(-10_000)));
        _pause = Button("Pause", TogglePause);
        controls.AddView(_pause);
        controls.AddView(Button("+10", () => Jump(10_000)));
        _volume = new SeekBar(this) { Max = 100, Progress = 80 };
        _volume.LayoutParameters = new LinearLayout.LayoutParams(220, ViewGroup.LayoutParams.WrapContent);
        _volume.ProgressChanged += (_, e) =>
        {
            if (e.FromUser)
                _session?.Engine.SetVolume(e.Progress);
        };
        controls.AddView(_volume);
        _clock = Label("0:00", 12);
        controls.AddView(_clock);
        controls.AddView(Button("Audio", () => PickTrack(true)));
        controls.AddView(Button("Subs", () => PickTrack(false)));
        controls.AddView(Button("1×", CycleSpeed));
        controls.AddView(Button("Replace", Replace));
        bar.AddView(controls);
        chrome.AddView(bar, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent, GravityFlags.Bottom));
        chrome.Touch += (_, args) =>
        {
            var motion = args.Event;
            if (motion is null || motion.Action != MotionEventActions.Up || _player is null)
                return;
            var y = motion.GetY();
            var x = motion.GetX();
            if (y > _player.Height - 280)
                return;
            if (x > _player.Width / 3f && x < _player.Width * 2f / 3f)
                TogglePause();
        };
        _player.AddView(chrome);
        root.AddView(_player);
        return root;
    }

    private void Save()
    {
        try
        {
            var ids = _session!.SaveLinks(_links?.Text ?? "");
            if (ids.Count == 0)
            {
                if (_status is not null)
                    _status.Text = "Paste a link first.";
                return;
            }

            if (_links is not null)
                _links.Text = "";
            Refresh();
            Open(ids[0]);
        }
        catch (Exception ex)
        {
            if (_status is not null)
                _status.Text = ex.Message;
        }
    }

    private void PlayOnce()
    {
        var line = First(_links?.Text);
        if (line.Length == 0 || _session is null)
            return;
        try
        {
            _playing = null;
            ShowPlayer(line);
            _video?.Post(() => _session.PlayOnce(line));
        }
        catch (Exception ex)
        {
            if (_status is not null)
                _status.Text = ex.Message;
        }
    }

    private void Open(Guid id)
    {
        if (_session is null)
            return;
        _playing = id;
        var title = _session.Library.Library().FirstOrDefault(item => item.MediaId == id)?.Title ?? "Playing";
        ShowPlayer(LibraryController.DisplayTitle(title));
        _video?.Post(() => _session.Play(id));
    }

    private void ShowPlayer(string title)
    {
        if (_title is not null)
            _title.Text = title;
        if (_home is not null)
            _home.Visibility = ViewStates.Gone;
        if (_player is not null)
            _player.Visibility = ViewStates.Visible;
        if (_next is not null)
            _next.Visibility = _session?.Next() is not null ? ViewStates.Visible : ViewStates.Gone;
        _seekTarget = -1;
    }

    private void Leave()
    {
        _session?.Stop();
        if (_player is not null)
            _player.Visibility = ViewStates.Gone;
        if (_home is not null)
            _home.Visibility = ViewStates.Visible;
        Refresh();
    }

    private void PlayNext()
    {
        if (_session?.Next() is Guid id)
            Open(id);
    }

    private void Replace()
    {
        if (_playing is not Guid id || _session is null)
        {
            if (_status is not null)
                _status.Text = "Save the title before replacing its link.";
            return;
        }

        var input = new EditText(this) { Hint = "New link" };
        new AlertDialog.Builder(this)?
            .SetTitle("Replace link")?
            .SetView(input)?
            .SetPositiveButton("Replace", (_, _) =>
            {
                var raw = input.Text?.Trim();
                if (string.IsNullOrWhiteSpace(raw))
                    return;
                try
                {
                    _session.Replace(id, raw);
                }
                catch (Exception ex)
                {
                    if (_status is not null)
                        _status.Text = ex.Message;
                }
            })?
            .SetNegativeButton("Cancel", (_, _) => { })?
            .Show();
    }

    private void CycleSpeed()
    {
        if (_session is null)
            return;
        var next = _session.Playback.Rate switch
        {
            < 1.1f => 1.25f,
            < 1.4f => 1.5f,
            < 1.8f => 2f,
            _ => 1f
        };
        _session.Playback.SetRate(next);
    }

    private void PickTrack(bool audio)
    {
        if (_session is null)
            return;
        var tracks = audio ? _session.Playback.AudioTracks : _session.Playback.SubtitleTracks;
        var names = tracks.Select(track => track.Name).ToArray();
        new AlertDialog.Builder(this)?
            .SetTitle(audio ? "Audio" : "Subtitles")?
            .SetItems(names, (_, e) =>
            {
                var track = tracks.ElementAtOrDefault(e.Which);
                if (track is null)
                    return;
                if (audio)
                    _session.Playback.SelectAudio(track.Id);
                else
                    _session.Playback.SelectSubtitle(track.Id);
            })?
            .Show();
    }

    private void Jump(long delta)
    {
        var current = _seekTarget >= 0 ? _seekTarget : _duration * (_seek?.Progress ?? 0) / Math.Max(1, _seek?.Max ?? 1);
        CommitSeek(Math.Clamp(current + delta, 0, Math.Max(0, _duration)));
    }

    private void CommitSeek(long position)
    {
        _seekOrigin = _duration * (_seek?.Progress ?? 0) / Math.Max(1, _seek?.Max ?? 1);
        _seekTarget = position;
        _seekTries = 0;
        _seekUntil = System.Environment.TickCount64 + 1600;
        _updatingSeek = true;
        if (_seek is not null && _duration > 0)
            _seek.Progress = (int)(position * _seek.Max / _duration);
        _updatingSeek = false;
        _session?.Playback.Seek(position, _duration);
    }

    private void OnSnapshot(object? sender, PlayerSnapshot snapshot)
    {
        _duration = snapshot.DurationMs;
        _paused = snapshot.IsPaused;
        if (_pause is not null)
            _pause.Text = _paused ? "Play" : "Pause";
        if (_clock is not null)
            _clock.Text = Format(snapshot.PositionMs) + " / " + Format(snapshot.DurationMs);
        if (_dragging || _updatingSeek || _seek is null)
            return;
        if (_seekTarget >= 0)
        {
            var landed = System.Environment.TickCount64 >= _seekUntil
                && (Math.Abs(snapshot.PositionMs - _seekTarget) < 4000
                    || (Math.Abs(snapshot.PositionMs - _seekTarget) < Math.Abs(_seekOrigin - _seekTarget) && Math.Abs(snapshot.PositionMs - _seekTarget) < 12000));
            if (!landed)
            {
                if (System.Environment.TickCount64 >= _seekUntil && _seekTries < 1)
                {
                    _seekTries++;
                    _seekUntil = System.Environment.TickCount64 + 1600;
                    _session?.Playback.Seek(_seekTarget, snapshot.DurationMs);
                }

                return;
            }

            _seekTarget = -1;
        }

        _updatingSeek = true;
        _seek.Progress = snapshot.DurationMs <= 0 ? 0 : (int)(snapshot.PositionMs * _seek.Max / snapshot.DurationMs);
        _updatingSeek = false;
    }

    private void TogglePause()
    {
        if (_session is null)
            return;
        if (_paused)
            _session.Playback.ResumePlayback();
        else
            _session.Playback.Pause();
    }

    private void Refresh()
    {
        if (_session is null || _continue is null || _library is null || _done is null)
            return;
        _continue.RemoveAllViews();
        _library.RemoveAllViews();
        _done.RemoveAllViews();
        foreach (var item in _session.Library.Continue())
            _continue.AddView(Tile(item.MediaId, LibraryController.DisplayTitle(item.Title)));
        foreach (var item in _session.Library.Library())
        {
            var tile = Tile(item.MediaId, LibraryController.DisplayTitle(item.Title));
            if (item.Completed)
                _done.AddView(tile);
            else
                _library.AddView(tile);
        }
    }

    private Button Tile(Guid id, string title)
    {
        var button = Button(title, () =>
        {
            try
            {
                Open(id);
            }
            catch (Exception ex)
            {
                if (_status is not null)
                    _status.Text = ex.Message;
            }
        });
        button.Gravity = GravityFlags.Left | GravityFlags.Bottom;
        return button;
    }

    private TextView Label(string text, int size)
    {
        var view = new TextView(this) { Text = text, TextSize = size };
        view.SetTextColor(Color.ParseColor("#EBEBEB"));
        view.SetPadding(0, 12, 0, 12);
        return view;
    }

    private Button Button(string text, Action click)
    {
        var button = new Button(this) { Text = text };
        button.Click += (_, _) => click();
        return button;
    }

    private LinearLayout Row()
    {
        var row = new LinearLayout(this) { Orientation = Orientation.Horizontal };
        row.SetPadding(0, 8, 0, 8);
        return row;
    }

    private static string First(string? text)
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

    private sealed class AndroidUi(Activity activity) : IUiMarshal
    {
        public void Post(Action action) => activity.RunOnUiThread(action);
    }
}
