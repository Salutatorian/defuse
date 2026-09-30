using System.Runtime.InteropServices;

namespace Defuse.Sources.Servers;

public static class UncConnector
{
    public static void Connect(string remotePath, string? username, string? password)
    {
        var resource = new NetResource
        {
            Scope = 0,
            Type = 1,
            DisplayType = 0,
            Usage = 0,
            RemoteName = remotePath
        };
        var result = WNetAddConnection2(ref resource, string.IsNullOrEmpty(password) ? null : password, string.IsNullOrEmpty(username) ? null : username, 0);
        if (result == 0)
            return;
        if (result == 1219)
            throw new InvalidOperationException("That network folder is already connected with different credentials.");
        throw new InvalidOperationException("Windows could not open that network folder.");
    }

    [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
    private static extern int WNetAddConnection2(ref NetResource resource, string? password, string? username, int flags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NetResource
    {
        public int Scope;
        public int Type;
        public int DisplayType;
        public int Usage;
        public string? LocalName;
        public string? RemoteName;
        public string? Comment;
        public string? Provider;
    }
}
