namespace LazyGit.Models;

/// <summary>Рабочая папка репозитория (git worktree).</summary>
/// <param name="Branch">Ветка («feature/x») или null — detached HEAD.</param>
/// <param name="IsMain">Основная папка (с .git); её нельзя удалить как worktree.</param>
/// <param name="Locked">Причина блокировки («» — без причины) или null — не заблокирован.</param>
/// <param name="Prunable">Папки больше нет (удалили вручную): причина или null.</param>
public sealed record Worktree(string Path, string Head, string? Branch, bool IsMain, bool IsBare, string? Locked, string? Prunable)
{
    public string ShortHead => Head[..Math.Min(7, Head.Length)];
}
