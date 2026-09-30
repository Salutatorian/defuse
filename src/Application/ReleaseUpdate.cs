using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using Defuse.Domain;

namespace Defuse.Application;

public static class ReleaseUpdate
{
    public static void InstallOnLaunch()
    {
        if (OperatingSystem.IsAndroid())
            return;
        var package = TryStage();
        if (package is null)
            return;
        try
        {
            StartReplacer(package);
            Environment.Exit(0);
        }
        catch (SystemException)
        {
        }
    }

    public static string? TryStage()
    {
        try
        {
            if (Debugger.IsAttached || IsDevelopmentLayout())
                return null;
            var platform = ReleaseChoice.Detect(
                OperatingSystem.IsWindows(),
                OperatingSystem.IsLinux(),
                OperatingSystem.IsMacOS(),
                OperatingSystem.IsAndroid(),
                RuntimeInformation.ProcessArchitecture,
                !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("FLATPAK_ID")),
                CanWriteInstallDirectory());
            var package = ReleaseChoice.PackageName(platform);
            if (package is null)
                return null;

            var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
            using var api = NewClient(TimeSpan.FromSeconds(8));
            using var response = api.GetAsync("https://api.github.com/repos/Salutatorian/defuse/releases/latest").GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;
            using var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            var tag = doc.RootElement.GetProperty("tag_name").GetString();
            if (!ReleaseChoice.TryNewer(current, tag, out _))
                return null;
            if (tag is null || !TryDigest(doc.RootElement, package, out var digest))
                return null;

            var url = new Uri($"https://github.com/Salutatorian/defuse/releases/download/{tag}/{package}");
            if (!ReleaseChoice.IsTrustedPackageUrl(url, tag, package))
                return null;

            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Defuse", "update");
            Directory.CreateDirectory(dir);
            var destination = Path.Combine(dir, package);
            if (!Download(url, destination) || !MatchesDigest(destination, digest))
            {
                if (File.Exists(destination))
                    File.Delete(destination);
                return null;
            }

            return destination;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            return null;
        }
    }

    private static bool TryDigest(JsonElement release, string package, out string? digest)
    {
        digest = null;
        if (!release.TryGetProperty("assets", out var assets))
            return false;
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.GetProperty("name").GetString() != package)
                continue;
            if (asset.TryGetProperty("digest", out var value))
                digest = value.GetString();
            return true;
        }

        return false;
    }

    private static bool Download(Uri url, string destination)
    {
        using var http = NewClient(TimeSpan.FromMinutes(20));
        using var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode || !IsPackageHost(response.RequestMessage?.RequestUri?.Host))
            return false;
        using var stream = response.Content.ReadAsStream();
        using var file = File.Create(destination);
        stream.CopyTo(file);
        return file.Length > 0;
    }

    private static bool MatchesDigest(string path, string? digest)
    {
        const string prefix = "sha256:";
        if (string.IsNullOrEmpty(digest))
            return true;
        if (!digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;
        var expected = Convert.FromHexString(digest[prefix.Length..]);
        using var stream = File.OpenRead(path);
        var actual = SHA256.HashData(stream);
        return expected.AsSpan().SequenceEqual(actual);
    }

    private static bool IsPackageHost(string? host)
    {
        if (string.IsNullOrEmpty(host))
            return false;
        if (host.Equals("github.com", StringComparison.OrdinalIgnoreCase))
            return true;
        return host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
    }

    private static void StartReplacer(string packagePath)
    {
        var appDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pid = Environment.ProcessId;
        var scriptDir = Path.GetDirectoryName(packagePath) ?? Path.GetTempPath();
        if (OperatingSystem.IsWindows())
        {
            var script = Path.Combine(scriptDir, "apply.ps1");
            var body = packagePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? $"$ErrorActionPreference = 'Stop'\r\nWait-Process -Id {pid} -ErrorAction SilentlyContinue\r\nStart-Process -FilePath {Ps(packagePath)} -ArgumentList '/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART' -Wait\r\n"
                : $"$ErrorActionPreference = 'Stop'\r\nWait-Process -Id {pid} -ErrorAction SilentlyContinue\r\n$stage = Join-Path $env:TEMP 'defuse-next'\r\nif (Test-Path -LiteralPath $stage) {{ Remove-Item -LiteralPath $stage -Recurse -Force }}\r\nExpand-Archive -LiteralPath {Ps(packagePath)} -DestinationPath $stage -Force\r\nCopy-Item -Path (Join-Path $stage '*') -Destination {Ps(appDir)} -Recurse -Force\r\nStart-Process -FilePath (Join-Path {Ps(appDir)} 'Defuse.exe') -WorkingDirectory {Ps(appDir)}\r\n";
            File.WriteAllText(script, body);
            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList = { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script },
                CreateNoWindow = true,
                UseShellExecute = false
            });
            return;
        }

        var sh = Path.Combine(scriptDir, "apply.sh");
        string shell;
        if (packagePath.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase))
            shell = $"#!/bin/sh\nwhile kill -0 {pid} 2>/dev/null; do sleep 0.2; done\nflatpak install --user -y --noninteractive {Sh(packagePath)}\nflatpak run com.defuse.Player >/dev/null 2>&1 &\n";
        else if (packagePath.EndsWith(".dmg", StringComparison.OrdinalIgnoreCase))
            shell = $"#!/bin/sh\nwhile kill -0 {pid} 2>/dev/null; do sleep 0.2; done\nmnt=$(mktemp -d)\nhdiutil attach {Sh(packagePath)} -mountpoint \"$mnt\" -nobrowse\ncp -R \"$mnt\"/. {Sh(appDir)}/\nhdiutil detach \"$mnt\"\nchmod 755 {Sh(appDir)}/Defuse\ncd {Sh(appDir)}\n./Defuse >/dev/null 2>&1 &\n";
        else
            shell = $"#!/bin/sh\nwhile kill -0 {pid} 2>/dev/null; do sleep 0.2; done\ntar -xzf {Sh(packagePath)} -C {Sh(appDir)} --no-absolute-names\nchmod 755 {Sh(appDir)}/Defuse\ncd {Sh(appDir)}\n./Defuse >/dev/null 2>&1 &\n";
        File.WriteAllText(sh, shell);
        if (packagePath.EndsWith(".flatpak", StringComparison.OrdinalIgnoreCase))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "flatpak-spawn",
                ArgumentList = { "--host", "/bin/sh", sh },
                UseShellExecute = false
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            ArgumentList = { sh },
            UseShellExecute = false
        });
    }

    private static HttpClient NewClient(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Defuse");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    private static bool IsDevelopmentLayout()
    {
        var dir = AppContext.BaseDirectory.Replace('\\', '/');
        return dir.Contains("/bin/Debug/", StringComparison.OrdinalIgnoreCase)
            || dir.Contains("/bin/Release/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanWriteInstallDirectory()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, ".defuse-write");
            using var file = File.Create(path, 1, FileOptions.DeleteOnClose);
            return file.CanWrite;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string Ps(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static string Sh(string value) => "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";
}
