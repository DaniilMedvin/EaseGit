namespace EaseGit.Models;

/// <summary>Шаг interactive rebase: коммит и действие над ним.</summary>
/// <param name="NewSubject">Новый заголовок для <see cref="RebaseAction.Reword"/>, иначе null. Тело сообщения сохраняется.</param>
public sealed record RebaseStep(Commit Commit, RebaseAction Action, string? NewSubject = null)
{
    /// <summary>Заголовок, который будет у коммита после rebase.</summary>
    public string Subject => Action == RebaseAction.Reword && NewSubject is not null ? NewSubject : Commit.Subject;
}
