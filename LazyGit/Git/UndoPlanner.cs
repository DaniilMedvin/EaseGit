using LazyGit.Models;

namespace LazyGit.Git;

/// <summary>Как отменить последнее действие: аргументы git и тексты для подтверждения.</summary>
/// <param name="Args">Команда git без «git».</param>
/// <param name="Question">Вопрос подтверждения: «Отменить коммит «feat: add a»?».</param>
/// <param name="Done">Сообщение после успешной отмены.</param>
/// <param name="RewritesHistory">Переписывает историю ветки: нужно подтверждение.</param>
public sealed record UndoPlan(string[] Args, string Question, string Done, bool RewritesHistory);

/// <summary>
/// Отмена последнего действия по записи reflog. Коммит отменяется мягко (изменения возвращаются в индекс),
/// merge и pull — с сохранением незакоммиченных изменений, checkout — переключением обратно.
/// </summary>
public static class UndoPlanner
{
    private const string Previous = "HEAD@{1}";

    /// <summary>План отмены или null и причина, почему действие так не отменить.</summary>
    public static (UndoPlan? Plan, string? Reason) Plan(ReflogEntry? last)
    {
        if (last is null)
            return (null, T("Отменять нечего: в истории HEAD ещё нет действий", "Nothing to undo: HEAD history has no actions yet"));

        var (action, details) = Split(last.Subject);
        if (action == "commit (initial)")
            return (null, T("Первый коммит так не отменить: у него нет предыдущего состояния", "The first commit can't be undone this way: there is no previous state"));

        if (action is "commit" or "commit (amend)")
        {
            var what = action == "commit" ? T("коммит", "commit") : T("amend коммита", "commit amend");
            return (new UndoPlan(["reset", "--soft", Previous], T($"Отменить {what} «{details}»?", $"Undo {what} '{details}'?"),
                T("Коммит отменён, его изменения снова в индексе", "Commit undone, its changes are staged again"), RewritesHistory: true), null);
        }

        // merge, pull и merge-коммит после конфликта: --keep сохраняет незакоммиченные изменения
        // и отказывается, если они пересекаются с откатываемыми.
        if (action.StartsWith("merge ") || action.StartsWith("pull") || action is "commit (merge)" or "cherry-pick")
        {
            var name = action switch
            {
                _ when action.StartsWith("merge ") => action,
                _ when action.StartsWith("pull") => "pull",
                "commit (merge)" => T("merge-коммит", "merge commit"),
                _ => "cherry-pick",
            };
            return (new UndoPlan(["reset", "--keep", Previous], T($"Отменить {name}?", $"Undo {name}?"),
                T($"Отменено: {name}", $"Undone: {name}"), RewritesHistory: true), null);
        }

        if (action == "checkout")
            return CheckoutBack(details);
        if (action == "reset")
            return (null, T("Последнее действие уже отмена (reset). Повтори его заново или посмотри git reflog", "The last action is already an undo (reset). Redo it or look at git reflog"));
        if (action.StartsWith("rebase"))
            return (null, T("Rebase так не отменить: найди нужное состояние в git reflog и сделай git reset --keep", "Rebase can't be undone this way: find the state you need in git reflog and run git reset --keep"));

        return (null, T($"Не знаю, как отменить «{last.Subject}». Посмотри git reflog", $"Don't know how to undo '{last.Subject}'. Look at git reflog"));
    }

    /// <summary>«checkout: moving from main to f» → переключиться обратно на main.</summary>
    private static (UndoPlan?, string?) CheckoutBack(string details)
    {
        const string prefix = "moving from ";
        var to = details.LastIndexOf(" to ", StringComparison.Ordinal);
        if (!details.StartsWith(prefix) || to < prefix.Length)
            return (null, T($"Не понял запись reflog «{details}»", $"Couldn't parse reflog entry '{details}'"));

        var from = details[prefix.Length..to];
        // Отсоединённый HEAD записывается хешем: на него переключаемся с --detach.
        var isHash = from.Length >= 7 && from.All(Uri.IsHexDigit);
        string[] args = isHash ? ["switch", "--detach", from] : ["switch", "--no-guess", from];
        return (new UndoPlan(args, T($"Вернуться на {from}?", $"Go back to {from}?"), T($"Вернулись на {from}", $"Back on {from}"), RewritesHistory: false), null);
    }

    /// <summary>«commit (amend): feat: b» → («commit (amend)», «feat: b»).</summary>
    private static (string Action, string Details) Split(string subject)
    {
        var colon = subject.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? (subject, "") : (subject[..colon], subject[(colon + 2)..]);
    }
}
