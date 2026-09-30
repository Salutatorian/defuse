using Avalonia;
using Defuse.Application;
using Defuse.Playback.Cross;

namespace Defuse.Cross;

sealed class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        ReleaseUpdate.InstallOnLaunch();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
