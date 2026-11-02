using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Недавно открытые в EaseGit проекты: где на диске лежит копия репозитория GitHub.</summary>
/// <param name="Paths">Папки недавних проектов, свежие первыми.</param>
public sealed record LocalProjects(GitRunner Git, IReadOnlyList<string> Paths)
{
    /// <summary>Папка проекта с remote на <paramref name="repo"/> или null.</summary>
    public Task<string?> FindAsync(GitHubRepo repo, CancellationToken ct = default) => LocalClones.FindAsync(Git, Paths, repo, ct);

    /// <summary>Репозитории GitHub недавних проектов с их папками; проекты без remote на GitHub пропускаются.</summary>
    public async Task<IReadOnlyList<(GitHubRepo Repo, string Path)>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<(GitHubRepo, string)>();
        foreach (var path in Paths.Where(Directory.Exists))
        {
            var url = await new GitService(Git, path).GetRemoteUrlAsync(ct);
            if (url is not null && GitHubRepo.FromRemoteUrl(url) is { } repo)
                result.Add((repo, path));
        }

        return result;
    }
}
