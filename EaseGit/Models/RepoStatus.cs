namespace EaseGit.Models;

/// <summary>Состояние репозитория: текущая ветка, upstream и изменённые файлы.</summary>
/// <param name="Branch">Имя текущей ветки или null в состоянии detached HEAD.</param>
/// <param name="HeadHash">Хеш HEAD или null в репозитории без коммитов.</param>
/// <param name="Upstream">Upstream-ветка, например origin/main, или null.</param>
/// <param name="Ahead">Количество коммитов, которых нет в upstream.</param>
/// <param name="Behind">Количество коммитов upstream, которых нет локально.</param>
public sealed record RepoStatus(
    string? Branch,
    string? HeadHash,
    string? Upstream,
    int Ahead,
    int Behind,
    IReadOnlyList<FileChange> Files)
{
    public bool IsDetached => Branch is null;

    public bool IsClean => Files.Count == 0;
}
