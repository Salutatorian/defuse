using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Defuse.Application;

namespace Defuse.Cross;

public partial class App : Avalonia.Application
{
    public static PlayerHost Host { get; private set; } = null!;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Defuse");
        Host = new PlayerHost(root);
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
            desktop.ShutdownRequested += (_, _) => Host.Dispose();
        }

        base.OnFrameworkInitializationCompleted();
    }
}

public sealed class UiMarshal : IUiMarshal
{
    public void Post(Action action) => Avalonia.Threading.Dispatcher.UIThread.Post(action);
}
