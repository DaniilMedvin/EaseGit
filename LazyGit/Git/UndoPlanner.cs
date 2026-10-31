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
            return (null, "Отменять нечего: в истории HEAD ещё нет действий");

        var (action, details) = Split(last.Subject);
        if (action == "commit (initial)")
            return (null, "Первый коммит так не отменить: у него нет предыдущего состояния");

        if (action is "commit" or "commit (amend)")
        {
            var what = action == "commit" ? "коммит" : "amend коммита";
            return (new UndoPlan(["reset", "--soft", Previous], $"Отменить {what} «{details}»?",
                "Коммит отменён, его изменения снова в индексе", RewritesHistory: true), null);
        }

        // merge, pull и merge-коммит после конфликта: --keep сохраняет незакоммиченные изменения
        // и отказывается, если они пересекаются с откатываемыми.
        if (action.StartsWith("merge ") || action.StartsWith("pull") || action is "commit (merge)" or "cherry-pick")
        {
            var name = action switch
            {
                _ when action.StartsWith("merge ") => action,
                _ when action.StartsWith("pull") => "pull",
                "commit (merge)" => "merge-коммит",
                _ => "cherry-pick",
            };
            return (new UndoPlan(["reset", "--keep", Previous], $"Отменить {name}?",
                $"Отменено: {name}", RewritesHistory: true), null);
        }

        if (action == "checkout")
            return CheckoutBack(details);
        if (action == "reset")
            return (null, "Последнее действие уже отмена (reset). Повтори его заново или посмотри git reflog");
        if (action.StartsWith("rebase"))
            return (null, "Rebase так не отменить: найди нужное состояние в git reflog и сделай git reset --keep");

        return (null, $"Не знаю, как отменить «{last.Subject}». Посмотри git reflog");
    }

    /// <summary>«checkout: moving from main to f» → переключиться обратно на main.</summary>
    private static (UndoPlan?, string?) CheckoutBack(string details)
    {
        const string prefix = "moving from ";
        var to = details.LastIndexOf(" to ", StringComparison.Ordinal);
        if (!details.StartsWith(prefix) || to < prefix.Length)
            return (null, $"Не понял запись reflog «{details}»");

        var from = details[prefix.Length..to];
        // Отсоединённый HEAD записывается хешем: на него переключаемся с --detach.
        var isHash = from.Length >= 7 && from.All(Uri.IsHexDigit);
        string[] args = isHash ? ["switch", "--detach", from] : ["switch", "--no-guess", from];
        return (new UndoPlan(args, $"Вернуться на {from}?", $"Вернулись на {from}", RewritesHistory: false), null);
    }

    /// <summary>«commit (amend): feat: b» → («commit (amend)», «feat: b»).</summary>
    private static (string Action, string Details) Split(string subject)
    {
        var colon = subject.IndexOf(": ", StringComparison.Ordinal);
        return colon < 0 ? (subject, "") : (subject[..colon], subject[(colon + 2)..]);
    }
}
