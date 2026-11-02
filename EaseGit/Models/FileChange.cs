namespace EaseGit.Models;

/// <summary>
/// Изменённый файл. Один файл может одновременно иметь staged и unstaged изменения,
/// поэтому состояние индекса и рабочей копии хранится отдельно.
/// </summary>
/// <param name="Path">Путь относительно корня репозитория.</param>
/// <param name="Staged">Изменение в индексе (X в git status).</param>
/// <param name="Unstaged">Изменение в рабочей копии (Y в git status).</param>
/// <param name="OriginalPath">Прежний путь для переименованных и скопированных файлов.</param>
public sealed record FileChange(string Path, ChangeKind Staged, ChangeKind Unstaged, string? OriginalPath = null)
{
    public bool HasStagedChanges => Staged is not ChangeKind.Unmodified and not ChangeKind.Untracked;

    public bool HasUnstagedChanges => Unstaged is not ChangeKind.Unmodified;

    public bool IsUntracked => Unstaged is ChangeKind.Untracked;

    public bool IsConflicted => Staged is ChangeKind.Unmerged || Unstaged is ChangeKind.Unmerged;
}
