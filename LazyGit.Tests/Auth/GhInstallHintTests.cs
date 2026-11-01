using System.Runtime.InteropServices;
using LazyGit.Auth;

namespace LazyGit.Tests.Auth;

public sealed class GhInstallHintTests
{
    [Theory]
    [InlineData("NAME=\"Fedora Linux\"\nID=fedora\nVERSION_ID=44\n", "sudo dnf install gh")]
    [InlineData("ID=ubuntu\nID_LIKE=debian\n", "sudo apt install gh")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"\n", "sudo apt install gh")]
    [InlineData("ID=\"rocky\"\nID_LIKE=\"rhel centos fedora\"\n", "sudo dnf install gh")]
    [InlineData("ID=arch\n", "sudo pacman -S github-cli")]
    [InlineData("ID=endeavouros\nID_LIKE=arch\n", "sudo pacman -S github-cli")]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"\n", "sudo zypper install gh")]
    [InlineData("ID=alpine\n", "sudo apk add github-cli")]
    [InlineData("ID=nixos\n", GhInstallHint.Url)]
    [InlineData(null, GhInstallHint.Url)]
    public void Command_Linux_PicksPackageManagerByDistro(string? osRelease, string expected)
    {
        Assert.Equal(expected, GhInstallHint.Command(OSPlatform.Linux, osRelease));
    }

    [Fact]
    public void Command_WindowsAndMac_UseTheirPackageManagers()
    {
        Assert.Equal("winget install GitHub.cli", GhInstallHint.Command(OSPlatform.Windows, null));
        Assert.Equal("brew install gh", GhInstallHint.Command(OSPlatform.OSX, null));
    }
}
