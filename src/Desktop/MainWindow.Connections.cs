using System.Windows;
using System.Windows.Input;
using Defuse.Application;
using Defuse.Domain;
using Defuse.Integrations;
using Defuse.Sources.Servers;
using Defuse.Sources.Stremio;
using Microsoft.Win32;

namespace Defuse.Desktop;

public partial class MainWindow
{
    private RemoteSession? _jellyfin;
    private RemoteSession? _emby;
    private Uri? _jellyfinServer;
    private Uri? _embyServer;
    private Dictionary<string, string?> _streamSubtitles = [];

    private void SaveClipboard(object sender, RoutedEventArgs e)
    {
        if (_loadingSettings)
            return;
        App.Library.SetClipboardWatch(ClipboardWatch.IsChecked == true);
    }

    private void ScanFolder(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Scan a folder" };
        if (dialog.ShowDialog() != true)
            return;
        var count = App.Library.ImportFolder(dialog.FolderName);
        Status($"Added {count} videos.");
        ShowLibrary(this, e);
    }

    private void ConnectUnc(object sender, RoutedEventArgs e)
    {
        try
        {
            UncConnector.Connect(UncPath.Text.Trim(), UncUser.Text.Trim(), UncPassword.Password);
            if (!Directory.Exists(UncPath.Text.Trim()))
            {
                Status("Windows could not open that network folder.");
                return;
            }

            var count = App.Library.ImportFolder(UncPath.Text.Trim());
            Status($"Added {count} videos from the network folder.");
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void ScanNfs(object sender, RoutedEventArgs e)
    {
        var path = NfsPath.Text.Trim();
        var message = NfsMount.Describe(path);
        if (!Directory.Exists(path))
        {
            Status(message);
            return;
        }

        var count = App.Library.ImportFolder(path);
        Status($"Added {count} videos. {message}");
    }

    private async void SaveAddon(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = new Uri(ManifestUrl.Text.Trim());
            var json = await App.Http.GetStringAsync(uri);
            var description = StremioManifest.ParseManifest(json);
            App.Library.Store.SaveAddon(Guid.NewGuid(), description.Name, uri.AbsoluteUri, UrlRedactor.Redact(uri), true);
            Status($"Saved {description.Name}.");
            ShowSettings(this, e);
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private async void FindStreams(object sender, RoutedEventArgs e)
    {
        if (AddonList.SelectedItem is not Row row || row.MediaId is not Guid addonId)
        {
            Status("Select a saved addon.");
            return;
        }

        var manifest = App.Library.Store.AddonManifest(addonId);
        if (manifest is null || string.IsNullOrWhiteSpace(StremioId.Text))
            return;
        try
        {
            var resource = StremioManifest.ResourceUri(new Uri(manifest), "stream", StremioType.Text.Trim(), StremioId.Text.Trim());
            var json = await App.Http.GetStringAsync(resource);
            var streams = StremioManifest.ParseStreams(json).Where(stream => !string.IsNullOrWhiteSpace(stream.Url)).ToArray();
            _streamSubtitles = [];
            foreach (var stream in streams)
                _streamSubtitles[stream.Url!] = stream.Subtitles?.FirstOrDefault();
            StreamList.ItemsSource = streams.Select(stream =>
            {
                var display = Uri.TryCreate(stream.Url, UriKind.Absolute, out var uri) ? UrlRedactor.Redact(uri) : "Stream";
                var label = string.IsNullOrWhiteSpace(stream.Title) ? display : $"{stream.Title}  {display}";
                return new Row(label, Payload: stream.Url);
            }).ToArray();
            if (streams.Length == 0)
                Status("That addon returned no playable streams.");
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void PlaySelectedStream(object sender, MouseButtonEventArgs e)
    {
        if (StreamList.SelectedItem is not Row row || string.IsNullOrWhiteSpace(row.Payload))
            return;
        var title = string.IsNullOrWhiteSpace(StremioId.Text) ? "Stream" : StremioId.Text.Trim();
        _streamSubtitles.TryGetValue(row.Payload, out var subtitle);
        var id = RemoteTitles.SaveLocator(App.Library.Store, title, row.Payload, "Stremio", true, subtitle);
        PlaySaved(id);
    }

    private async void ListJellyfin(object sender, RoutedEventArgs e)
    {
        try
        {
            _jellyfinServer = new Uri(JellyfinUrl.Text.Trim());
            _jellyfin = await new JellyfinClient(App.Http).AuthenticateAsync(_jellyfinServer, JellyfinUser.Text.Trim(), JellyfinPassword.Password, CancellationToken.None);
            var items = await new JellyfinClient(App.Http).ListAsync(_jellyfinServer, _jellyfin.AccessToken, _jellyfin.UserId, CancellationToken.None);
            JellyfinList.ItemsSource = items.Select(item => new Row(item.Title, Payload: item.Id)).ToArray();
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void PlayJellyfin(object sender, MouseButtonEventArgs e) => PlayServerItem(JellyfinList.SelectedItem as Row, _jellyfinServer, _jellyfin, "Jellyfin");

    private async void ListEmby(object sender, RoutedEventArgs e)
    {
        try
        {
            _embyServer = new Uri(EmbyUrl.Text.Trim());
            _emby = await new EmbyClient(App.Http).AuthenticateAsync(_embyServer, EmbyUser.Text.Trim(), EmbyPassword.Password, CancellationToken.None);
            var items = await new EmbyClient(App.Http).ListAsync(_embyServer, _emby.AccessToken, _emby.UserId, CancellationToken.None);
            EmbyList.ItemsSource = items.Select(item => new Row(item.Title, Payload: item.Id)).ToArray();
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void PlayEmby(object sender, MouseButtonEventArgs e) => PlayServerItem(EmbyList.SelectedItem as Row, _embyServer, _emby, "Emby");

    private async void ListPlex(object sender, RoutedEventArgs e)
    {
        try
        {
            var items = await new PlexClient(App.Http).ListLibrariesAsync(new Uri(PlexUrl.Text.Trim()), PlexToken.Password, CancellationToken.None);
            PlexList.ItemsSource = items.Select(item => item.Title).ToArray();
            Status("Plex libraries are listed. Playback is not started from this list.");
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private async void ListWebDav(object sender, RoutedEventArgs e)
    {
        try
        {
            var hrefs = await new WebDavClient(App.Http).ListAsync(new Uri(WebDavUrl.Text.Trim()), WebDavUser.Text.Trim(), WebDavPassword.Password, CancellationToken.None);
            WebDavList.ItemsSource = hrefs.Select(href => new Row(href, Payload: href)).ToArray();
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void PlayWebDav(object sender, MouseButtonEventArgs e)
    {
        if (WebDavList.SelectedItem is not Row row || string.IsNullOrWhiteSpace(row.Payload))
            return;
        if (!Uri.TryCreate(row.Payload, UriKind.Absolute, out var uri) || uri.Scheme is not "http" and not "https")
        {
            Status("That WebDAV entry is not a direct video link.");
            return;
        }

        try
        {
            var saved = App.Library.Save(new SaveRequest(uri.AbsoluteUri, Path.GetFileName(uri.AbsolutePath), "video", null, null, null, null, null, null, null, true));
            PlaySaved(saved.MediaId);
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void SaveFtp(object sender, RoutedEventArgs e)
    {
        try
        {
            var uri = PlaybackUris.FtpFile(FtpHost.Text.Trim(), 21, FtpPath.Text.Trim(), string.IsNullOrWhiteSpace(FtpUser.Text) ? null : FtpUser.Text.Trim());
            var id = RemoteTitles.SaveLocator(App.Library.Store, Path.GetFileName(uri.AbsolutePath), uri.AbsoluteUri, "Ftp", false);
            OpenDetails(id);
            Status("FTP link saved. The password was not added to it.");
        }
        catch (Exception ex)
        {
            Status(LogScrubber.Scrub(ex.Message));
        }
    }

    private void ListSftp(object sender, RoutedEventArgs e)
    {
        try
        {
            var port = int.TryParse(SftpPort.Text, out var parsed) ? parsed : 22;
            var files = SftpBrowser.List(SftpHost.Text.Trim(), port, SftpUser.Text.Trim(), SftpPassword.Password, SftpPath.Text.Trim());
            SftpList.ItemsSource = files;
        }
        catch (InvalidOperationException ex)
        {
            Status(ex.Message);
        }
    }

    private void SaveKeys(object sender, RoutedEventArgs e)
    {
        App.SaveSecret("secret.tmdb", TmdbKey.Password);
        App.SaveSecret("secret.trakt", TraktToken.Password);
        TmdbKey.Password = "";
        TraktToken.Password = "";
        Status(TraktQueue.CanSend(App.ReadSecret("secret.trakt"))
            ? "Keys saved on this PC. Trakt scrobbles stay queued until you send them."
            : "Keys saved on this PC.");
    }

    private void CreateProfile(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProfileName.Text))
            return;
        var id = App.Library.Store.CreateProfile(ProfileName.Text);
        if (!string.IsNullOrEmpty(ProfilePin.Password))
            App.Library.Store.SetPin(id, App.Protector.Protect(ProfilePin.Password));
        ProfilePin.Password = "";
        ShowSettings(this, e);
    }

    private void SwitchProfile(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Row row || row.MediaId is not Guid id)
            return;
        var stored = App.Library.Store.GetPin(id);
        if (stored is not null && App.Protector.Unprotect(stored) != ProfilePin.Password)
        {
            Status("That PIN does not match.");
            return;
        }

        App.Library.Store.SwitchProfile(id);
        ProfilePin.Password = "";
        Status("Profile switched.");
        ShowHome(this, e);
    }

    private void SetProfilePin(object sender, RoutedEventArgs e)
    {
        if (ProfileList.SelectedItem is not Row row || row.MediaId is not Guid id)
            return;
        var pin = string.IsNullOrEmpty(ProfilePin.Password) ? null : App.Protector.Protect(ProfilePin.Password);
        App.Library.Store.SetPin(id, pin);
        ProfilePin.Password = "";
        ShowSettings(this, e);
    }

    private void ExportLibrary(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "JSON|*.json", FileName = "defuse-library.json" };
        if (dialog.ShowDialog() != true)
            return;
        var titles = App.Library.Library().Select(item => new ExportTitle(item.Title, item.Type, item.PositionMs, item.Completed, null)).ToArray();
        File.WriteAllText(dialog.FileName, LibraryExchange.ToJson(titles));
        Status("Exported titles without links.");
    }

    private void PlayServerItem(Row? row, Uri? server, RemoteSession? session, string kind)
    {
        if (row is null || server is null || session is null || string.IsNullOrWhiteSpace(row.Payload))
            return;
        var locator = PlaybackUris.JellyfinVideo(server, row.Payload, session.AccessToken);
        var id = RemoteTitles.SaveLocator(App.Library.Store, row.Label, locator.AbsoluteUri, kind, true);
        PlaySaved(id);
    }
}
