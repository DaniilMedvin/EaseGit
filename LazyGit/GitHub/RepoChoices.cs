using System.Text.Json;
using LazyGit.Models;

namespace LazyGit.GitHub;

/// <summary>Список для выбора репозитория: недавние локальные и свои с GitHub. Чистые функции без IO.</summary>
public static class RepoChoices
{
    public const string ListFields = "nameWithOwner,description,isPrivate";

    /// <summary>Разбор gh repo list --json.</summary>
    public static IReadOnlyList<RepoChoice> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        var choices = new List<RepoChoice>();
        foreach (var repo in document.RootElement.EnumerateArray())
        {
            var name = repo.TryGetProperty("nameWithOwner", out var value) ? value.GetString() : null;
            var parts = name?.Split('/');
            if (parts is not [var owner, var repoName])
                continue;

            var description = repo.TryGetProperty("description", out var text) && text.ValueKind == JsonValueKind.String ? text.GetString() ?? "" : "";
            var isPrivate = repo.TryGetProperty("isPrivate", out var flag) && flag.ValueKind == JsonValueKind.True;
            choices.Add(new RepoChoice(new GitHubRepo(owner, repoName), null, description, isPrivate));
        }

        return choices;
    }

    /// <summary>Сначала недавние локальные (в порядке открытия), затем свои с GitHub; у своих, что есть локально, — путь.</summary>
    public static IReadOnlyList<RepoChoice> Merge(IReadOnlyList<(GitHubRepo Repo, string Path)> locals, IReadOnlyList<RepoChoice> remote)
    {
        var result = new List<RepoChoice>();
        foreach (var (repo, path) in locals)
        {
            if (result.Any(c => LocalClones.SameRepo(c.Repo, repo)))
                continue;

            var info = remote.FirstOrDefault(c => LocalClones.SameRepo(c.Repo, repo));
            result.Add(new RepoChoice(repo, path, info?.Description ?? "", info?.IsPrivate ?? false));
        }

        result.AddRange(remote.Where(c => !result.Any(r => LocalClones.SameRepo(r.Repo, c.Repo))));
        return result;
    }

    /// <summary>
    /// Строки, подходящие под ввод (подстрока имени без учёта регистра). Если введено «владелец/репо», которого нет в списке, —
    /// он первой строкой: так можно открыть любой чужой репозиторий.
    /// </summary>
    public static IReadOnlyList<RepoChoice> Filter(IReadOnlyList<RepoChoice> all, string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return all;

        var found = all.Where(c => c.Repo.NameWithOwner.Contains(text, StringComparison.OrdinalIgnoreCase)
            || c.Description.Contains(text, StringComparison.OrdinalIgnoreCase)).ToList();
        var parts = text.Split('/');
        if (parts is [{ Length: > 0 } owner, { Length: > 0 } name] && !parts.Any(p => p.Contains(' '))
            && !all.Any(c => string.Equals(c.Repo.NameWithOwner, text, StringComparison.OrdinalIgnoreCase)))
            found.Insert(0, new RepoChoice(new GitHubRepo(owner, name), null, Manual: true));
        return found;
    }
}
