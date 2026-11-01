namespace LazyGit.Models;

/// <summary>Репозиторий на GitHub: владелец и имя.</summary>
public sealed record GitHubRepo(string Owner, string Name)
{
    /// <summary>«owner/name» — так его понимает gh --repo.</summary>
    public string NameWithOwner => $"{Owner}/{Name}";

    public override string ToString() => NameWithOwner;

    /// <summary>
    /// Репозиторий из адреса remote: https://github.com/o/r(.git), git@github.com:o/r.git, ssh://git@github.com/o/r.git.
    /// Не GitHub — null.
    /// </summary>
    public static GitHubRepo? FromRemoteUrl(string url)
    {
        var text = url.Trim();
        string? path = null;
        foreach (var prefix in (string[])["https://github.com/", "http://github.com/", "ssh://git@github.com/", "git@github.com:", "git://github.com/"])
        {
            if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                path = text[prefix.Length..];
                break;
            }
        }

        return path is null ? null : FromPath(path);
    }

    /// <summary>Репозиторий из адреса PR или страницы: https://github.com/o/r/pull/12 → o/r.</summary>
    public static GitHubRepo? FromWebUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? FromPath(uri.AbsolutePath.TrimStart('/')) : null;

    private static GitHubRepo? FromPath(string path)
    {
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return null;

        var name = parts[1].EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? parts[1][..^4] : parts[1];
        return name.Length == 0 ? null : new GitHubRepo(parts[0], name);
    }
}
