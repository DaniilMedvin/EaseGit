namespace EaseGit.Models;

/// <summary>Незавершённая операция git: обычно остановилась на конфликте и ждёт решения.</summary>
public enum RepoOperation
{
    None,
    Merge,
    CherryPick,
    Revert,
    Rebase,
}
