using Renci.SshNet;

namespace Defuse.Sources.Servers;

public static class SftpBrowser
{
    public static IReadOnlyList<string> List(string host, int port, string username, string password, string path)
    {
        try
        {
            using var client = new SftpClient(host, port, username, password);
            client.Connect();
            var folder = string.IsNullOrWhiteSpace(path) ? "." : path;
            return client.ListDirectory(folder)
                .Where(file => file.Name is not "." and not "..")
                .Select(file => file.FullName)
                .ToArray();
        }
        catch (Exception)
        {
            throw new InvalidOperationException("Could not list that SFTP folder.");
        }
    }
}
