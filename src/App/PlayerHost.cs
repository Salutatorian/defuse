using Defuse.Shell;

namespace Defuse.Cross;

public sealed class PlayerHost : IDisposable
{
    public PlayerHost(string root)
    {
        Session = PlayerSession.Open(new UiMarshal(), root, null);
    }

    public PlayerSession Session { get; }

    public void Dispose() => Session.Dispose();
}
