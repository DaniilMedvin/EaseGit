namespace LazyGit.Models;

/// <summary>Решение по ревью PR (reviewDecision в GitHub).</summary>
public enum ReviewState
{
    /// <summary>Ревью не требуется или ещё не было.</summary>
    None,
    Required,
    Approved,
    ChangesRequested,
}
