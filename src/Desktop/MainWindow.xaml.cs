using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Defuse.Application;
using Defuse.Domain;
using Defuse.Integrations;
using Defuse.Metadata.TMDB;
using Defuse.Sources.Direct;
using Microsoft.Win32;

namespace Defuse.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _chromeTimer;
    private bool _chromeHeld;
    private int _subtitleMargin = -1;
    private float _rateBeforeFast = 1;
    private bool _fastFromHold;
    private bool _fastFromSpace;
    private bool _fastHoldPending;
    private bool _holdFromRight;
    private bool _spaceHoldPending;
    private DispatcherTimer? _fastHoldTimer;
    private DispatcherTimer? _spaceHoldTimer;
    private bool _volumeArmed;
    private Point _volumeStart;
    private double _volumeOrigin;
    private readonly DispatcherTimer _feedbackTimer;
    private readonly DispatcherTimer _volumeSaveTimer;
    private bool _rememberVolume;
    private bool _allowQueueAdvance;
    private long? _creditsMs;
    private bool _markedFinished;
    private int _sceneLookup;
    private readonly DispatcherTimer _seekTimer;
    private readonly bool _reducedMotion;
    private Guid? _mediaId;
    private string? _showTitle;
    private bool _playerOpen;
    private bool _updatingSeek;
    private bool _loadingSettings;
    private bool _compact;
    private Rect _preCompactBounds;
    private double _compactAspect = 16d / 9d;
    private string _section = "home";
    private bool _fullScreen;
    private WindowState _savedState;
    private Rect _savedBounds;
    private long _positionMs;
    private long _durationMs;
    private long? _introEndMs;
    private Guid? _nextEpisodeId;
    private PlayTarget? _playing;
    private bool _persistPlay;
    private string? _clipboardSeen;
    private int _coverPass;
    private IReadOnlyList<TmdbMatch> _matches = [];

    public MainWindow()
    {
        InitializeComponent();
        _reducedMotion = !SystemParameters.ClientAreaAnimation;
        _chromeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _chromeTimer.Tick += (_, _) =>
        {
            if (_reducedMotion || FailurePanel.Visibility == Visibility.Visible)
                return;
            if (MorePanel.Visibility == Visibility.Visible
                || QueuePanel.Visibility == Visibility.Visible
                || PlayerSourcePanel.Visibility == Visibility.Visible
                || PlayerReplacePanel.Visibility == Visibility.Visible)
                return;
            if (_chromeHeld)
            {
                _chromeTimer.Stop();
                return;
            }
            HideChrome();
            SeekHint.Visibility = Visibility.Collapsed;
        };
        _volumeSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _volumeSaveTimer.Tick += (_, _) =>
        {
            _volumeSaveTimer.Stop();
            PlaybackLook.Current.RememberVolume((int)VolumeSlider.Value);
        };
        VolumeSlider.Value = Math.Clamp(PlaybackLook.Current.Volume, 0, 100);
        _rememberVolume = true;
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _feedbackTimer.Tick += (_, _) =>
        {
            CenterFeedback.Visibility = Visibility.Collapsed;
            _feedbackTimer.Stop();
        };
        _seekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _seekTimer.Tick += (_, _) =>
        {
            _seekTimer.Stop();
            FinishSliderSeek();
        };
        RateList.ItemsSource = new[] { "0.5×", "1×", "1.25×", "1.5×", "2×" };
        FillKeyGuide();
        SizeChanged += (_, _) =>
        {
            FitCompactBar();
            FillVideo();
            if (!_compact || !_playerOpen)
                return;
            Dispatcher.BeginInvoke(() =>
            {
                if (_playerOpen && _compact)
                    PlaceSubtitles(PlayerBar.Opacity > 0);
            }, DispatcherPriority.Loaded);
        };
        App.Engine.SnapshotChanged += OnSnapshot;
        App.Engine.LoadingChanged += OnEngineLoading;
        App.Engine.Faulted += OnFault;
        App.Engine.Ended += (_, _) => OnPlaybackEnded();
        SystemEvents.PowerModeChanged += OnPower;
        Closed += (_, _) =>
        {
            SystemEvents.PowerModeChanged -= OnPower;
            if (_playerOpen)
                LeavePlayer(this, new RoutedEventArgs());
        };
        DataObject.AddPastingHandler(AddUrl, PasteAddLink);
        AllowDrop = true;
        PreviewDragOver += (_, args) =>
        {
            args.Effects = args.Data.GetDataPresent(DataFormats.Text) || args.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            args.Handled = true;
        };
        Drop += OnDrop;
        TmdbCredit.Text = TmdbClient.Attribution;
        CloudNote.Text = CloudCatalog.NotConnected + " " + string.Join(", ", CloudCatalog.Providers) + ".";
        ShowHome(this, new RoutedEventArgs());
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ContentRendered += (_, _) =>
        {
            if (_playerOpen)
                return;
            Dispatcher.BeginInvoke(UseShellBackdrop, DispatcherPriority.ApplicationIdle);
        };
        UseShellBackdrop();
        if (PresentationSource.FromVisual(this) is HwndSource source)
            source.AddHook(CornerHitTest);
    }

    private IntPtr CornerHitTest(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_SIZING = 0x0214;
        const int WM_NCHITTEST = 0x0084;
        if (msg == WM_SIZING && _compact && !_fullScreen && WindowState == WindowState.Normal)
        {
            var rect = Marshal.PtrToStructure<NativeRect>(lParam);
            LockCompactRect(ref rect, wParam.ToInt32());
            Marshal.StructureToPtr(rect, lParam, false);
            return IntPtr.Zero;
        }

        if (msg != WM_NCHITTEST || _fullScreen || WindowState != WindowState.Normal)
            return IntPtr.Zero;

        var packed = lParam.ToInt32();
        var dip = PointFromScreen(new Point(unchecked((short)(packed & 0xFFFF)), unchecked((short)((packed >> 16) & 0xFFFF))));
        const double grip = 22;
        var left = dip.X <= grip;
        var right = dip.X >= ActualWidth - grip;
        var top = dip.Y <= grip;
        var bottom = dip.Y >= ActualHeight - grip;
        var hit = 0;
        if (top && left)
            hit = 13;
        else if (top && right)
            hit = 14;
        else if (bottom && left)
            hit = 16;
        else if (bottom && right)
            hit = 17;
        if (hit == 0)
            return IntPtr.Zero;
        handled = true;
        return new IntPtr(hit);
    }

    private void UseShellBackdrop()
    {
        var kind = WindowBackdrop.Apply(this, opaqueVideo: false);
        if (kind == WindowBackdrop.Kind.Solid)
        {
            Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17));
            Tint.Background = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x17));
            return;
        }

        Background = Brushes.Transparent;
        Tint.Background = Brushes.Transparent;
    }

    private void MinimizeWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseWindow(object sender, RoutedEventArgs e) => Close();

    private void UseVideoSurface()
    {
        _ = WindowBackdrop.Apply(this, opaqueVideo: true);
        Background = Brushes.Black;
        Tint.Background = Brushes.Black;
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        if (!App.Library.ClipboardWatch() || !Clipboard.ContainsText())
            return;
        var text = Clipboard.GetText().Trim();
        if (text == _clipboardSeen)
            return;
        _clipboardSeen = text;
        var link = LinkClassifier.Classify(text);
        if (link.IsPlayableNow || link.Class == LinkClass.UnknownHttp)
            Status("A video link is on the clipboard. Add link can use it.");
    }

    private void ShowHome(object sender, RoutedEventArgs e)
    {
        Show("home");
        App.Library.RefreshParsedTitles();
        BindHome();
        _ = FillCovers();
    }

    private void BindHome()
    {
        var profile = App.Library.Store.ActiveProfileId;
        var library = App.Library.Store.ListLibrary().ToArray();
        var offHome = library
            .Where(item => HomeShelf.IsHidden(profile, item.MediaId.ToString("D"))
                || (!string.IsNullOrWhiteSpace(item.ShowTitle) && HomeShelf.IsHidden(profile, "show:" + item.ShowTitle)))
            .Select(item => item.MediaId)
            .ToHashSet();
        library = library.Where(item => !offHome.Contains(item.MediaId)).ToArray();
        var finishedShows = library
            .Where(item => !string.IsNullOrWhiteSpace(item.ShowTitle))
            .GroupBy(item => item.ShowTitle!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.All(item => item.Completed))
            .Select(group => group.FirstOrDefault(item => item.PosterPath is not null) ?? group.First())
            .ToArray();
        var finishedNames = finishedShows.Select(item => item.ShowTitle!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var done = library
            .Where(item => string.IsNullOrWhiteSpace(item.ShowTitle) && item.Completed)
            .Select(Tile)
            .Concat(finishedShows.Select(item => new TitleTile(item.MediaId, item.ShowTitle!, LoadPoster(item.PosterPath), 0, item.ShowTitle)))
            .ToArray();
        DoneHeading.Visibility = done.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        DoneList.Visibility = DoneHeading.Visibility;
        DoneList.ItemsSource = done;
        ContinueList.ItemsSource = App.Library.Continue().Where(item => !offHome.Contains(item.MediaId)).Select(Tile).ToArray();
        RecentList.ItemsSource = library
            .Where(item => string.IsNullOrWhiteSpace(item.ShowTitle) ? !item.Completed : !finishedNames.Contains(item.ShowTitle!))
            .Take(30)
            .Reverse()
            .Select(Tile)
            .ToArray();
    }

    private void ShowLibrary(object sender, RoutedEventArgs e)
    {
        Show("library");
        var store = App.Library.Store;
        App.Library.RefreshParsedTitles();
        LibraryList.ItemsSource = store.ListLibrary().Select(Tile).ToArray();
        _ = FillCovers();
        var profile = store.ActiveProfileId;
        CollectionList.ItemsSource = store.Collections(profile).Select(card => new Row($"{card.Name} ({card.Count})", OtherId: card.Id)).ToArray();
        PlaylistList.ItemsSource = store.Playlists(profile).Select(card => new Row($"{card.Name} ({card.Count})", OtherId: card.Id)).ToArray();
    }

    private void ShowSettings(object sender, RoutedEventArgs e)
    {
        Show("settings");
        _loadingSettings = true;
        ClipboardWatch.IsChecked = App.Library.ClipboardWatch();
        ProfileList.ItemsSource = App.Library.Store.Profiles().Select(card => new Row(card.HasPin ? $"{card.Name} (PIN)" : card.Name, card.Id)).ToArray();
        AddonList.ItemsSource = App.Library.Store.Addons().Select(card => new Row(card.Name, card.Id)).ToArray();
        _loadingSettings = false;
    }

    private void OpenDetails(Guid mediaId)
    {
        _mediaId = mediaId;
        App.Library.RefreshParsedTitles();
        var store = App.Library.Store;
        var item = store.ListLibrary().FirstOrDefault(entry => entry.MediaId == mediaId);
        var target = store.GetPlayTarget(mediaId);
        if (item is null || target is null)
        {
            Status("That title is no longer in the library.");
            ShowLibrary(this, new RoutedEventArgs());
            return;
        }

        _showTitle = item.ShowTitle;
        Show("details");
        var code = item.EpisodeNumber is int episode ? $"S{item.SeasonNumber:00}E{episode:00}" : null;
        DetailTitle.Text = LibraryController.DisplayTitle(item.Title);
        DetailMeta.Text = code is null ? item.Type : "";
        DetailSource.Text = item.RedactedLocator;
        FavoriteButton.Content = item.Favorite ? "Unfavorite" : "Favorite";
        PosterImage.Source = LoadPoster(item.PosterPath);
        var watched = item.DurationMs is > 0 && item.PositionMs is long position
            ? Math.Clamp(position * 100d / item.DurationMs.Value, 0, 100)
            : 0;
        DetailProgress.Value = watched;
        DetailProgress.Visibility = watched > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (item.PosterPath is null)
            _ = FillCovers();
        var intro = store.GetIntro(mediaId);
        IntroSeconds.Text = intro is null ? "" : (intro.Value / 1000d).ToString("0");
    }

    private void DismissHome(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: TitleTile tile })
            return;
        var profile = App.Library.Store.ActiveProfileId;
        HomeShelf.Hide(profile, string.IsNullOrWhiteSpace(tile.ShowTitle) ? tile.MediaId.ToString("D") : "show:" + tile.ShowTitle);
        ShowHome(this, e);
    }

    private void OpenSelectedDone(object sender, MouseButtonEventArgs e)
    {
        if (DoneList.SelectedItem is not TitleTile tile)
            return;
        if (!string.IsNullOrWhiteSpace(tile.ShowTitle))
        {
            _showTitle = tile.ShowTitle;
            OpenShowFromDetails(this, new RoutedEventArgs());
            return;
        }

        OpenDetails(tile.MediaId);
    }

    private void PlaySelectedContinue(object sender, MouseButtonEventArgs e)
    {
        if (ContinueList.SelectedItem is TitleTile tile)
            PlaySaved(tile.MediaId);
    }

    private void OpenSelectedRecent(object sender, MouseButtonEventArgs e)
    {
        if (RecentList.SelectedItem is TitleTile tile)
            PlaySaved(tile.MediaId);
    }

    private void OpenSelectedLibrary(object sender, MouseButtonEventArgs e)
    {
        if (LibraryList.SelectedItem is TitleTile tile)
            OpenDetails(tile.MediaId);
    }

    private void PlayDetails(object sender, RoutedEventArgs e)
    {
        if (_mediaId is Guid id)
            PlaySaved(id);
    }

    private void ToggleFavorite(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id)
            return;
        var favorite = FavoriteButton.Content as string != "Unfavorite";
        App.Library.SetFavorite(id, favorite);
        OpenDetails(id);
    }

    private void MarkWatched(object sender, RoutedEventArgs e)
    {
        if (_mediaId is Guid id)
            App.Library.MarkWatched(id);
    }

    private void MarkUnwatched(object sender, RoutedEventArgs e)
    {
        if (_mediaId is Guid id)
            App.Library.MarkUnwatched(id);
    }

    private void DeleteDetails(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id)
            return;
        App.Library.Delete(id);
        _mediaId = null;
        ShowLibrary(this, e);
    }

    private void OpenShowFromDetails(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_showTitle))
        {
            Status("This title is not part of a show.");
            return;
        }

        Show("show");
        ShowHeading.Text = _showTitle;
        EpisodeList.ItemsSource = App.Library.Store.Episodes(_showTitle)
            .OrderBy(item => item.SeasonNumber ?? 0)
            .ThenBy(item => item.EpisodeNumber ?? 0)
            .Select(ItemRow)
            .ToArray();
    }

    private void PlaySelectedEpisode(object sender, MouseButtonEventArgs e)
    {
        if (EpisodeList.SelectedItem is Row row && row.MediaId is Guid id)
            PlaySaved(id);
    }

    private void CreateCollection(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(CollectionName.Text))
            return;
        App.Library.Store.CreateCollection(App.Library.Store.ActiveProfileId, CollectionName.Text);
        ShowLibrary(this, e);
    }

    private void CreatePlaylist(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(PlaylistName.Text))
            return;
        App.Library.Store.CreatePlaylist(App.Library.Store.ActiveProfileId, PlaylistName.Text);
        ShowLibrary(this, e);
    }

    private void AddSelectedToCollection(object sender, RoutedEventArgs e) =>
        AddSelected(CollectionList.SelectedItem as Row, App.Library.Store.AddToCollection);

    private void AddSelectedToPlaylist(object sender, RoutedEventArgs e) =>
        AddSelected(PlaylistList.SelectedItem as Row, App.Library.Store.AddToPlaylist);

    private void SaveIntro(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id || !double.TryParse(IntroSeconds.Text, out var seconds))
        {
            Status("Enter the intro length in seconds.");
            return;
        }

        App.Library.Store.SetIntro(id, (long)(seconds * 1000));
        Status("Intro end saved.");
    }

    private void CloseScreeners()
    {
        AddOverlay.Visibility = Visibility.Collapsed;
        SearchOverlay.Visibility = Visibility.Collapsed;
        DetailSourceOverlay.Visibility = Visibility.Collapsed;
        ReplaceOverlay.Visibility = Visibility.Collapsed;
    }

    private void OpenScreener(Grid overlay)
    {
        CloseScreeners();
        overlay.Visibility = Visibility.Visible;
    }

    private void KeepScreenerOpen(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void OpenAdd(object sender, RoutedEventArgs e)
    {
        OpenScreener(AddOverlay);
        if (string.IsNullOrWhiteSpace(AddUrl.Text) && Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            var link = LinkClassifier.Classify(text);
            if (link.IsPlayableNow || link.Class is LinkClass.UnknownHttp or LinkClass.ExternalPage)
                AddUrl.Text = text;
        }
    }

    private void CloseAdd(object sender, RoutedEventArgs e) => AddOverlay.Visibility = Visibility.Collapsed;

    private void PasteAddLink(object sender, DataObjectPastingEventArgs e)
    {
        var raw = e.DataObject.GetData(DataFormats.UnicodeText) as string
            ?? e.DataObject.GetData(DataFormats.Text) as string;
        if (string.IsNullOrWhiteSpace(raw))
            return;
        var text = raw.Replace("\r\n", "\n").Replace('\r', '\n').Trim();
        if (text.Length == 0)
            return;
        var insertAt = AddUrl.SelectionStart;
        var atLineStart = insertAt == 0 || AddUrl.Text[insertAt - 1] is '\n' or '\r';
        if (!atLineStart)
            text = "\n" + text;
        if (!text.EndsWith('\n'))
            text += "\n";
        e.DataObject = new DataObject(DataFormats.UnicodeText, text.Replace("\n", "\r\n"));
    }

    private void SaveAdd(object sender, RoutedEventArgs e)
    {
        var lines = AddUrl.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0)
            .ToArray();
        if (lines.Length == 0)
            return;
        var ids = new List<Guid>();
        try
        {
            foreach (var line in lines)
                ids.Add(App.Library.Save(App.Library.FromUrl(line)).MediaId);
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
            if (ids.Count == 0)
                return;
        }

        WatchQueue.Replace(ids);
        AddOverlay.Visibility = Visibility.Collapsed;
        OpenDetails(ids[0]);
    }

    private void PlayOnce(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = App.Library.CreateTransient(AddUrl.Text, null, true);
            AddOverlay.Visibility = Visibility.Collapsed;
            BeginPlayback(target, 0, false);
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void OpenSearch(object sender, RoutedEventArgs e)
    {
        OpenScreener(SearchOverlay);
        SearchBox.Text = "";
        SearchList.ItemsSource = null;
        SearchBox.Focus();
    }

    private void CloseSearch(object sender, RoutedEventArgs e) => SearchOverlay.Visibility = Visibility.Collapsed;

    private void SearchChanged(object sender, TextChangedEventArgs e)
    {
        SearchList.ItemsSource = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? null
            : App.Library.Search(SearchBox.Text).Select(ItemRow).ToArray();
    }

    private void OpenSearchResult(object sender, MouseButtonEventArgs e)
    {
        if (SearchList.SelectedItem is not Row row || row.MediaId is not Guid id)
            return;
        SearchOverlay.Visibility = Visibility.Collapsed;
        OpenDetails(id);
    }

    private void ShowDetailSources(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id)
            return;
        DetailSourceList.ItemsSource = SourceRows(id);
        OpenScreener(DetailSourceOverlay);
    }

    private void CloseDetailSources(object sender, RoutedEventArgs e) => DetailSourceOverlay.Visibility = Visibility.Collapsed;

    private void PlayDetailSource(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid mediaId || DetailSourceList.SelectedItem is not Row row || row.OtherId is not Guid sourceId)
            return;
        var target = App.Library.Store.OpenSource(mediaId, sourceId);
        if (target is null)
            return;
        DetailSourceOverlay.Visibility = Visibility.Collapsed;
        BeginPlayback(target, ResumeStart(target), true);
    }

    private void OpenReplace(object sender, RoutedEventArgs e)
    {
        ReplaceUrl.Text = "";
        OpenScreener(ReplaceOverlay);
    }

    private void CloseReplace(object sender, RoutedEventArgs e) => ReplaceOverlay.Visibility = Visibility.Collapsed;

    private void ApplyReplace(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id)
            return;
        try
        {
            App.Library.Replace(id, ReplaceUrl.Text);
            ReplaceOverlay.Visibility = Visibility.Collapsed;
            OpenDetails(id);
            Status("Link replaced. Your place is unchanged.");
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void ChoosePoster(object sender, RoutedEventArgs e)
    {
        if (_mediaId is not Guid id)
            return;
        var dialog = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp" };
        if (dialog.ShowDialog() != true)
            return;
        var ext = Path.GetExtension(dialog.FileName).ToLowerInvariant();
        if (ext is not ".png" and not ".jpg" and not ".jpeg" and not ".webp")
        {
            Status("Poster must be a PNG, JPG, or WebP image.");
            return;
        }

        Directory.CreateDirectory(AppPaths.Artwork);
        var dest = Path.Combine(AppPaths.Artwork, id.ToString("N") + ext);
        File.Copy(dialog.FileName, dest, overwrite: true);
        App.Library.Store.SetPoster(id, dest);
        PosterImage.Source = LoadPoster(dest);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) && e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
        {
            var added = 0;
            var skipped = 0;
            foreach (var path in files)
            {
                if (Directory.Exists(path))
                {
                    added += App.Library.ImportFolder(path);
                    continue;
                }

                if (!File.Exists(path) || LinkClassifier.Classify(path).Class != LinkClass.LocalFile)
                {
                    skipped++;
                    continue;
                }

                try
                {
                    added += App.Library.ImportFile(path);
                }
                catch (Exception ex)
                {
                    skipped++;
                    Status(LogScrubber.Scrub(ex.Message));
                }
            }

            if (!_playerOpen)
                ShowHome(this, new RoutedEventArgs());
            if (added > 0)
                Status(added == 1 ? "Added 1 movie to Home." : $"Added {added} movies to Home.");
            else if (skipped > 0)
                Status("Drop a movie file, such as mp4, mov, or mkv.");
            else
                Status("Already on Home.");
            return;
        }

        if (e.Data.GetDataPresent(DataFormats.Text) && e.Data.GetData(DataFormats.Text) is string text)
        {
            AddUrl.Text = text.Trim();
            OpenScreener(AddOverlay);
        }
    }

    private async void LookupPoster(object sender, RoutedEventArgs e)
    {
        var key = App.ReadSecret("secret.tmdb");
        if (string.IsNullOrWhiteSpace(key) || _mediaId is null)
        {
            Status("Save a TMDB key in Settings first.");
            return;
        }

        try
        {
            _matches = await new TmdbClient(App.Http).SearchMovieAsync(key, DetailTitle.Text, null, CancellationToken.None);
            MatchList.ItemsSource = _matches.Select(match => $"{match.Title} {(match.Year is int year ? year.ToString() : "")}").ToArray();
            if (_matches.Count == 0)
                Status("No poster matches for that title.");
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private async void ApplyPoster(object sender, MouseButtonEventArgs e)
    {
        if (_mediaId is not Guid id || MatchList.SelectedIndex < 0 || MatchList.SelectedIndex >= _matches.Count)
            return;
        var match = _matches[MatchList.SelectedIndex];
        if (string.IsNullOrWhiteSpace(match.PosterPath))
        {
            Status("That match has no poster.");
            return;
        }

        try
        {
            var bytes = await App.Http.GetByteArrayAsync(new Uri($"https://image.tmdb.org/t/p/w342{match.PosterPath}"));
            var dest = Path.Combine(AppPaths.Artwork, id.ToString("N") + ".jpg");
            await File.WriteAllBytesAsync(dest, bytes);
            App.Library.Store.SetPoster(id, dest);
            PosterImage.Source = LoadPoster(dest);
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void Show(string name)
    {
        if (name is "home" or "library" or "settings")
            _section = name;

        var settings = _section == "settings";
        if (name is "details" or "show" && _section == "settings")
            _section = "library";
        settings = _section == "settings";
        BrowsePanes.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        SettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;
        HomeView.Visibility = _section == "home" ? Visibility.Visible : Visibility.Collapsed;
        LibraryView.Visibility = _section == "library" ? Visibility.Visible : Visibility.Collapsed;
        var shelf = !settings && name is not "details" and not "show";
        ListColumn.Width = shelf ? new GridLength(1, GridUnitType.Star) : new GridLength(380);
        SplitColumn.Width = shelf ? new GridLength(0) : new GridLength(8);
        DetailColumn.MinWidth = shelf ? 0 : 340;
        DetailColumn.Width = shelf ? new GridLength(0) : new GridLength(1, GridUnitType.Star);

        if (name == "details")
        {
            DetailsView.Visibility = Visibility.Visible;
            ShowView.Visibility = Visibility.Collapsed;
            EmptyDetail.Visibility = Visibility.Collapsed;
        }
        else if (name == "show")
        {
            DetailsView.Visibility = Visibility.Collapsed;
            ShowView.Visibility = Visibility.Visible;
            EmptyDetail.Visibility = Visibility.Collapsed;
        }
        else if (_mediaId is null)
        {
            DetailsView.Visibility = Visibility.Collapsed;
            ShowView.Visibility = Visibility.Collapsed;
            EmptyDetail.Visibility = Visibility.Visible;
        }

        SectionTitle.Text = _section switch
        {
            "library" => "Library",
            "settings" => "Settings",
            _ => "Home"
        };
        HomeButton.Background = BrushFor(_section == "home");
        LibraryButton.Background = BrushFor(_section == "library");
        SettingsButton.Background = BrushFor(_section == "settings");
        CloseScreeners();
    }

    private static Brush BrushFor(bool active) =>
        active ? new SolidColorBrush(Color.FromArgb(0x66, 0x3A, 0x6E, 0xC8)) : Brushes.Transparent;

    private void AddSelected(Row? container, Action<Guid, Guid> add)
    {
        if (LibraryList.SelectedItem is not TitleTile title || container?.OtherId is not Guid containerId)
        {
            Status("Select a title and a collection or playlist.");
            return;
        }

        add(containerId, title.MediaId);
        ShowLibrary(this, new RoutedEventArgs());
    }

    private SaveRequest ReadAddRequest() => App.Library.FromUrl(AddUrl.Text);

    private static Row ItemRow(LibraryItem item)
    {
        var mark = item.Favorite ? "★ " : "";
        return new Row(mark + LibraryController.DisplayTitle(item.Title), item.MediaId);
    }

    private static TitleTile Tile(LibraryItem item)
    {
        var progress = item.DurationMs is > 0 && item.PositionMs is long position
            ? Math.Clamp(position * 100d / item.DurationMs.Value, 0, 100)
            : 0;
        return new TitleTile(item.MediaId, LibraryController.DisplayTitle(item.Title), LoadPoster(item.PosterPath), progress);
    }

    private static TitleTile Tile(ContinueItem item)
    {
        var progress = item.DurationMs > 0 ? Math.Clamp(item.PositionMs * 100d / item.DurationMs, 0, 100) : 0;
        return new TitleTile(item.MediaId, LibraryController.DisplayTitle(item.Title), LoadPoster(item.PosterPath), progress);
    }

    private async Task FillCovers()
    {
        var pass = ++_coverPass;
        var missing = App.Library.Store.ListLibrary()
            .Where(item => item.PosterPath is null)
            .Select(item => (item.MediaId, Name: LibraryController.DisplayTitle(item.ShowTitle ?? item.Title)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Name))
            .Take(8)
            .ToArray();
        foreach (var item in missing)
        {
            if (pass != _coverPass)
                return;
            try
            {
                var dest = Path.Combine(AppPaths.Artwork, item.MediaId.ToString("N") + ".jpg");
                var saved = await CoverArt.SavePoster(App.Http, item.Name, dest, CancellationToken.None);
                if (saved is null)
                    continue;
                App.Library.Store.SetPoster(item.MediaId, saved);
                if (_mediaId == item.MediaId)
                    PosterImage.Source = LoadPoster(saved);
            }
            catch (Exception ex)
            {
                Status(LogScrubber.Scrub(ex.Message));
            }
        }

        if (pass != _coverPass || missing.Length == 0)
            return;
        if (_section == "home")
            BindHome();
        else if (_section == "library")
            LibraryList.ItemsSource = App.Library.Store.ListLibrary().Select(Tile).ToArray();
    }

    private static Row[] SourceRows(Guid mediaId) =>
        App.Library.Store.ListSources(mediaId)
            .Select(source => new Row($"{(source.Preferred ? "Preferred  " : "")}{source.Kind}  {source.RedactedLocator}", OtherId: source.SourceId))
            .ToArray();

    private static BitmapImage? LoadPoster(string? path)
    {
        if (path is not string file || !File.Exists(file))
            return null;
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.UriSource = new Uri(file);
        image.EndInit();
        image.Freeze();
        return image;
    }

    private void Status(string message) => StatusText.Text = LogScrubber.Scrub(message);

    private void OnPower(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend && _playerOpen)
            Dispatcher.Invoke(App.Playback.Flush);
    }

    private sealed record Row(string Label, Guid? MediaId = null, Guid? OtherId = null, string? Payload = null)
    {
        public override string ToString() => Label;
    }

    private sealed class TitleTile
    {
        public TitleTile(Guid mediaId, string title, ImageSource? poster, double progress, string? showTitle = null)
        {
            MediaId = mediaId;
            Title = title;
            Poster = poster;
            Progress = progress;
            ShowTitle = showTitle;
            ShowBar = progress > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public Guid MediaId { get; }
        public string Title { get; }
        public string? ShowTitle { get; }
        public ImageSource? Poster { get; }
        public double Progress { get; }
        public Visibility ShowBar { get; }
    }
}
