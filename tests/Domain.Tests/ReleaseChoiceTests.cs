using System.Runtime.InteropServices;
using Defuse.Domain;

namespace Defuse.Domain.Tests;

public class ReleaseChoiceTests
{
    [Fact]
    public void Each_system_gets_its_own_package()
    {
        Assert.Equal("Defuse-windows-x64-portable.zip", ReleaseChoice.PackageName(ReleasePlatform.WindowsPortable));
        Assert.Equal("Defuse-windows-x64-setup.exe", ReleaseChoice.PackageName(ReleasePlatform.WindowsInstaller));
        Assert.Equal("Defuse-linux-arm64.tar.gz", ReleaseChoice.PackageName(ReleasePlatform.LinuxArm64));
        Assert.Equal("Defuse-linux-x64.tar.gz", ReleaseChoice.PackageName(ReleasePlatform.LinuxX64));
        Assert.Equal("Defuse-linux-x64.flatpak", ReleaseChoice.PackageName(ReleasePlatform.LinuxFlatpak));
        Assert.Equal("Defuse-macos-arm64.dmg", ReleaseChoice.PackageName(ReleasePlatform.MacArm64));
        Assert.Equal("Defuse-android.apk", ReleaseChoice.PackageName(ReleasePlatform.Android));
    }

    [Fact]
    public void Flatpak_and_a_locked_windows_folder_pick_their_installers()
    {
        Assert.Equal(ReleasePlatform.LinuxFlatpak, ReleaseChoice.Detect(false, true, false, false, Architecture.X64, true, true));
        Assert.Equal(ReleasePlatform.WindowsInstaller, ReleaseChoice.Detect(true, false, false, false, Architecture.X64, false, false));
        Assert.Equal(ReleasePlatform.WindowsPortable, ReleaseChoice.Detect(true, false, false, false, Architecture.X64, false, true));
        Assert.Equal(ReleasePlatform.None, ReleaseChoice.Detect(false, false, true, false, Architecture.X64, false, true));
    }

    [Fact]
    public void Only_a_newer_numbered_release_is_installed()
    {
        Assert.True(ReleaseChoice.TryNewer(new Version(1, 0, 0, 0), "v1.0.1", out var latest));
        Assert.Equal(new Version(1, 0, 1), latest);
        Assert.False(ReleaseChoice.TryNewer(new Version(1, 0, 0), "v1.0.0", out _));
        Assert.False(ReleaseChoice.TryNewer(new Version(1, 0, 0), "v1.0.0-beta", out _));
        Assert.False(ReleaseChoice.TryNewer(new Version(1, 0, 0), "../v1.0.1", out _));
    }

    [Fact]
    public void Package_url_must_be_this_repository()
    {
        var package = "Defuse-windows-x64-portable.zip";
        var trusted = new Uri($"https://github.com/Salutatorian/defuse/releases/download/v1.0.1/{package}");
        Assert.True(ReleaseChoice.IsTrustedPackageUrl(trusted, "v1.0.1", package));
        Assert.False(ReleaseChoice.IsTrustedPackageUrl(new Uri("http://github.com/Salutatorian/defuse/releases/download/v1.0.1/" + package), "v1.0.1", package));
        Assert.False(ReleaseChoice.IsTrustedPackageUrl(new Uri("https://example.com/Defuse-windows-x64-portable.zip"), "v1.0.1", package));
    }
}
