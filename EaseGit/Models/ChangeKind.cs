namespace EaseGit.Models;

/// <summary>Вид изменения файла в индексе или рабочей копии (коды XY из git status).</summary>
public enum ChangeKind
{
    Unmodified,
    Modified,
    Added,
    Deleted,
    Renamed,
    Copied,
    TypeChanged,
    Unmerged,
    Untracked,
}
