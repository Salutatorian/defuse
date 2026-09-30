using System.Windows;
using Defuse.Application;

namespace Defuse.Desktop;

public sealed class WpfUiMarshal : IUiMarshal
{
    public void Post(Action action)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            action();
        else
            dispatcher.BeginInvoke(action);
    }
}
