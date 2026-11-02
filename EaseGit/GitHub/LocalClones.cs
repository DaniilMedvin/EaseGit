using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Локальные копии репозиториев GitHub среди недавно открытых в EaseGit.</summary>
public static class LocalClones
{
    /// <summary>Первый из <paramref name="paths"/>, чей remote указывает на <paramref name="repo"/>, или null.</summary>
    public static async Task<string?> FindAsync(GitRunner git, IEnumerable<string> paths, GitHubRepo repo, CancellationToken ct = default)
    {
        foreach (var path in paths.Where(Directory.Exists))
        {
            var url = await new GitService(git, path).GetRemoteUrlAsync(ct);
            if (url is not null && GitHubRepo.FromRemoteUrl(url) is { } found && SameRepo(found, repo))
                return path;
        }

        return null;
    }

    /// <summary>GitHub не различает регистр в имени владельца и репозитория.</summary>
    public static bool SameRepo(GitHubRepo a, GitHubRepo b) =>
        string.Equals(a.NameWithOwner, b.NameWithOwner, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Куда клонировать: рядом с <paramref name="nearProject"/> (обычно там лежат все проекты) или в домашнюю папку.
    /// Если папка с именем репозитория занята — «имя-владелец».
    /// </summary>
    public static string ClonePath(string? nearProject, GitHubRepo repo)
    {
        var parent = nearProject is not null && Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(nearProject)) is { Length: > 0 } dir
            ? dir
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var path = Path.Combine(parent, repo.Name);
        return Directory.Exists(path) ? Path.Combine(parent, $"{repo.Name}-{repo.Owner}") : path;
    }
}
