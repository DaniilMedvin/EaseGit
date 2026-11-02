using System.Runtime.InteropServices;

namespace EaseGit.Auth;

/// <summary>Как установить gh на этой системе: команда менеджера пакетов или, если он неизвестен, сайт.</summary>
public static class GhInstallHint
{
    public const string Url = "https://cli.github.com";

    /// <summary>Команда для текущей системы. На Linux дистрибутив определяется по /etc/os-release.</summary>
    public static string Command()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Command(OSPlatform.Windows, null);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Command(OSPlatform.OSX, null);

        string? osRelease = null;
        try
        {
            osRelease = File.ReadAllText("/etc/os-release");
        }
        catch (IOException)
        {
            // Нет файла — дистрибутив неизвестен, подскажем сайт.
        }
        catch (UnauthorizedAccessException)
        {
        }

        return Command(OSPlatform.Linux, osRelease);
    }

    /// <param name="osRelease">Содержимое /etc/os-release: по ID и ID_LIKE выбирается менеджер пакетов.</param>
    public static string Command(OSPlatform os, string? osRelease)
    {
        if (os == OSPlatform.Windows)
            return "winget install GitHub.cli";
        if (os == OSPlatform.OSX)
            return "brew install gh";

        // ID — сам дистрибутив, ID_LIKE — на что он похож (у Linux Mint: «ubuntu debian»).
        var ids = (osRelease ?? "").Split('\n')
            .Select(line => line.Split('=', 2))
            .Where(pair => pair.Length == 2 && pair[0].Trim() is "ID" or "ID_LIKE")
            .SelectMany(pair => pair[1].Trim().Trim('"', '\'').Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        foreach (var id in ids)
        {
            switch (id)
            {
                case "fedora" or "rhel" or "centos":
                    return "sudo dnf install gh";
                case "debian" or "ubuntu":
                    return "sudo apt install gh";
                case "arch":
                    return "sudo pacman -S github-cli";
                case "opensuse" or "suse" or "opensuse-tumbleweed" or "opensuse-leap":
                    return "sudo zypper install gh";
                case "alpine":
                    return "sudo apk add github-cli";
            }
        }

        return Url;
    }
}
