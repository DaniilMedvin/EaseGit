namespace EaseGit.Models;

/// <summary>Итог проверок CI у PR: худшее из всех.</summary>
public enum CheckState
{
    /// <summary>Проверок нет.</summary>
    None,
    Pending,
    Success,
    Failure,
}
