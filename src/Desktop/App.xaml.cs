using System.Net.Http;
using System.Windows;
using Defuse.Application;
using Defuse.Persistence;
using Defuse.Playback.LibVLC;

namespace Defuse.Desktop;

public partial class App : System.Windows.Application
{
    public static LibraryController Library { get; private set; } = null!;
    public static PlaybackCoordinator Playback { get; private set; } = null!;
    public static LibVlcEngine Engine { get; private set; } = null!;
    public static DpapiSecretProtector Protector { get; private set; } = null!;
    public static HttpClient Http { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show(args.Exception.Message, "Defuse");
            args.Handled = true;
        };

        Directory.CreateDirectory(AppPaths.Root);
        Protector = new DpapiSecretProtector();
        var store = new SqliteLibraryStore(AppPaths.Database, Protector);
        store.Initialize();
        Directory.CreateDirectory(AppPaths.Artwork);
        Library = new LibraryController(store, AppPaths.Artwork);
        Engine = new LibVlcEngine(new WpfUiMarshal());
        Playback = new PlaybackCoordinator(Engine, store);

        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            Playback.Stop();
        }
        catch (InvalidOperationException)
        {
        }

        Engine.Dispose();
        base.OnExit(e);
    }

    public static void SaveSecret(string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            Library.Store.SetSetting(key, null);
        else
            Library.Store.SetSetting(key, Convert.ToBase64String(Protector.Protect(value.Trim())));
    }

    public static string? ReadSecret(string key)
    {
        var stored = Library.Store.GetSetting(key);
        if (string.IsNullOrWhiteSpace(stored))
            return null;
        try
        {
            return Protector.Unprotect(Convert.FromBase64String(stored));
        }
        catch (FormatException)
        {
            return null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }
}
