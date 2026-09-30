using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Defuse.Domain;

public enum ReleasePlatform
{
    None,
    WindowsPortable,
    WindowsInstaller,
    LinuxX64,
    LinuxArm64,
    LinuxFlatpak,
    MacArm64,
    Android
}

public static partial class ReleaseChoice
{
    public static ReleasePlatform Detect(bool windows, bool linux, bool mac, bool android, Architecture architecture, bool flatpak, bool canWriteInstallDirectory)
    {
        if (android)
            return ReleasePlatform.Android;
        if (windows)
            return canWriteInstallDirectory ? ReleasePlatform.WindowsPortable : ReleasePlatform.WindowsInstaller;
        if (linux)
        {
            if (flatpak)
                return architecture == Architecture.X64 ? ReleasePlatform.LinuxFlatpak : ReleasePlatform.None;
            if (architecture == Architecture.Arm64)
                return ReleasePlatform.LinuxArm64;
            if (architecture == Architecture.X64)
                return ReleasePlatform.LinuxX64;
            return ReleasePlatform.None;
        }
        if (mac && architecture == Architecture.Arm64)
            return ReleasePlatform.MacArm64;
        return ReleasePlatform.None;
    }

    public static string? PackageName(ReleasePlatform platform) => platform switch
    {
        ReleasePlatform.None => null,
        ReleasePlatform.WindowsPortable => "Defuse-windows-x64-portable.zip",
        ReleasePlatform.WindowsInstaller => "Defuse-windows-x64-setup.exe",
        ReleasePlatform.LinuxX64 => "Defuse-linux-x64.tar.gz",
        ReleasePlatform.LinuxArm64 => "Defuse-linux-arm64.tar.gz",
        ReleasePlatform.LinuxFlatpak => "Defuse-linux-x64.flatpak",
        ReleasePlatform.MacArm64 => "Defuse-macos-arm64.dmg",
        ReleasePlatform.Android => "Defuse-android.apk",
        _ => throw new ArgumentOutOfRangeException(nameof(platform), platform, null)
    };

    public static bool TryNewer(Version current, string? tag, out Version latest)
    {
        latest = new Version(0, 0);
        if (string.IsNullOrEmpty(tag) || !TagPattern().IsMatch(tag))
            return false;
        if (!Version.TryParse(tag[1..], out var parsed))
            return false;
        latest = new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
        var running = new Version(current.Major, current.Minor, Math.Max(current.Build, 0));
        return latest > running;
    }

    public static bool IsTrustedPackageUrl(Uri uri, string tag, string packageName)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(uri.IdnHost, "github.com", StringComparison.OrdinalIgnoreCase))
            return false;
        var path = $"/Salutatorian/defuse/releases/download/{tag}/{packageName}";
        return string.Equals(uri.AbsolutePath, path, StringComparison.Ordinal);
    }

    [GeneratedRegex("^v[0-9]+\\.[0-9]+\\.[0-9]+$")]
    private static partial Regex TagPattern();
}
