using LazyGit.Models;

namespace LazyGit.Git;

/// <summary>
/// План interactive rebase → файл git-rebase-todo. Чистые функции без IO.
/// Шаги везде в порядке панели коммитов: новые сверху. В todo git ждёт обратный порядок — старые первыми.
/// </summary>
public static class RebaseTodo
{
    /// <summary>
    /// Почему план нельзя выполнить, или null. Squash и fixup вливаются в коммит ниже (старше),
    /// поэтому у самого старого оставшегося коммита их быть не может.
    /// </summary>
    public static string? Validate(IReadOnlyList<RebaseStep> steps)
    {
        var kept = steps.Where(s => s.Action != RebaseAction.Drop).ToList();
        if (kept.Count == 0)
            return T("Нельзя убрать все коммиты: для этого есть reset (H в панели коммитов)", "You can't drop every commit: use reset for that (H in the commits panel)");
        if (kept[^1].Action is RebaseAction.Squash or RebaseAction.Fixup)
            return T($"Коммит {kept[^1].Commit.ShortHash} самый старый: squash / fixup некуда влить. Оставь его pick", $"Commit {kept[^1].Commit.ShortHash} is the oldest: squash / fixup has nothing to merge into. Keep it as pick");
        if (steps.FirstOrDefault(s => s.Action == RebaseAction.Reword && string.IsNullOrWhiteSpace(s.NewSubject)) is { } empty)
            return T($"У коммита {empty.Commit.ShortHash} пустой заголовок", $"Commit {empty.Commit.ShortHash} has an empty subject");
        return null;
    }

    /// <summary>
    /// Самый старый коммит диапазона: его родителя нет среди шагов. После перестановки он может быть не последним,
    /// а rebase надо начинать от его родителя.
    /// </summary>
    public static Commit Oldest(IReadOnlyList<RebaseStep> steps)
    {
        var hashes = steps.Select(s => s.Commit.Hash).ToHashSet();
        return steps.Select(s => s.Commit).First(c => c.Parents.Count == 0 || !hashes.Contains(c.Parents[0]));
    }

    /// <summary>Изменилось ли что-нибудь: действие или порядок коммитов.</summary>
    public static bool HasChanges(IReadOnlyList<RebaseStep> steps, IReadOnlyList<Commit> original) =>
        steps.Any(s => s.Action != RebaseAction.Pick)
        || !steps.Select(s => s.Commit.Hash).SequenceEqual(original.Select(c => c.Hash));

    /// <summary>
    /// Текст todo. Reword делается не командой reword (она открыла бы редактор), а pick + exec git commit --amend
    /// с сообщением из файла <paramref name="messageFile"/>(хеш коммита).
    /// </summary>
    public static string Build(IReadOnlyList<RebaseStep> steps, Func<Commit, string> messageFile)
    {
        var lines = new List<string>();
        foreach (var step in steps.Reverse())
        {
            var hash = step.Commit.Hash;
            switch (step.Action)
            {
                case RebaseAction.Reword:
                    lines.Add($"pick {hash}");
                    lines.Add($"exec {RewordCommand(messageFile(step.Commit))}");
                    break;
                default:
                    lines.Add($"{step.Action.ToString().ToLowerInvariant()} {hash}");
                    break;
            }
        }

        return string.Join('\n', lines) + "\n";
    }

    /// <summary>Сообщение после reword: новый заголовок и прежнее тело.</summary>
    public static string RewordMessage(string subject, string body) =>
        body.Trim().Length == 0 ? subject.Trim() + "\n" : $"{subject.Trim()}\n\n{body.Trim()}\n";

    /// <summary>--only без путей: меняется только сообщение, даже если в индексе что-то есть.</summary>
    private static string RewordCommand(string file) =>
        $"git commit --amend --only --cleanup=strip --file={ShellQuote(file)}";

    /// <summary>Строка в одинарных кавычках для sh: todo и GIT_SEQUENCE_EDITOR выполняет shell.</summary>
    public static string ShellQuote(string text) => "'" + text.Replace("'", "'\\''") + "'";

    /// <summary>Итог плана для подтверждения: «1 squash, 2 drop, порядок изменён».</summary>
    public static string Summary(IReadOnlyList<RebaseStep> steps, IReadOnlyList<Commit> original)
    {
        var parts = steps.Where(s => s.Action != RebaseAction.Pick)
            .GroupBy(s => s.Action)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}")
            .ToList();
        if (!steps.Select(s => s.Commit.Hash).SequenceEqual(original.Select(c => c.Hash)))
            parts.Add(T("порядок изменён", "order changed"));
        return string.Join(", ", parts);
    }
}
