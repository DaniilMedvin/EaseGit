namespace EaseGit.Models;

/// <summary>Строка выбора репозитория: репозиторий, локальная копия (если есть) и описание с GitHub.</summary>
/// <param name="LocalPath">Папка недавно открытого проекта с этим remote или null — только на GitHub.</param>
/// <param name="Manual">Введён вручную «владелец/репо»: его нет ни среди своих, ни среди недавних.</param>
public sealed record RepoChoice(GitHubRepo Repo, string? LocalPath, string Description = "", bool IsPrivate = false, bool Manual = false);
