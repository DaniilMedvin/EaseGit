using LazyGit.Models;

namespace LazyGit.Highlighting;

/// <summary>
/// Сообщение коммита по изменённому коду: заголовок по главному изменению, тело — список всех.
/// Сообщения коммитов пишут по-английски (Conventional Commits), поэтому глаголы английские при любом языке интерфейса.
/// </summary>
public static class CommitSuggester
{
    public const int MaxBodyLines = 15;

    /// <summary>Предложение или null, если изменений в C#-коде нет.</summary>
    public static CommitSuggestion? Suggest(IReadOnlyList<(string Path, IReadOnlyList<CodeChange> Changes)> files)
    {
        var all = files.SelectMany(f => f.Changes.Select(c => (f.Path, Change: c))).ToList();
        if (all.Count == 0)
            return null;

        var onlyTests = files.All(f => IsTest(f.Path));
        var type = onlyTests ? "test" : all.Any(c => c.Change.Kind == CodeChangeKind.Added) ? "feat" : "refactor";
        var lines = all.Select(c => $"- {Verb(c.Change.Kind)} {Short(c.Change.Name)}").Distinct().ToList();
        var body = lines.Count <= 1 ? "" : string.Join('\n', lines.Take(MaxBodyLines))
            + (lines.Count > MaxBodyLines ? $"\n- and {lines.Count - MaxBodyLines} more" : "");
        return new CommitSuggestion(type, Subject(all.Select(c => c.Change).ToList(), files.Count), body);
    }

    /// <summary>
    /// Одно изменение — «add Cache.LoadAsync»; все в одном типе — «add X and Y» или «update Cache»;
    /// иначе — по самому частому глаголу: «add Cache, Store» или «update 3 files».
    /// </summary>
    private static string Subject(IReadOnlyList<CodeChange> changes, int files)
    {
        if (changes.Count == 1)
            return $"{Verb(changes[0].Kind)} {Short(changes[0].Name)}";

        var kinds = changes.Select(c => c.Kind).Distinct().ToList();
        var verb = kinds.Count == 1 ? Verb(kinds[0]) : "update";
        var owners = changes.Select(c => Owner(c.Name)).Distinct().ToList();
        if (owners.Count == 1)
        {
            var members = changes.Select(c => Member(c.Name)).Distinct().ToList();
            return kinds.Count == 1 && members.Count == 2 ? $"{verb} {owners[0]}.{members[0]} and {members[1]}" : $"{verb} {owners[0]}";
        }

        return owners.Count <= 3 ? $"{verb} {string.Join(", ", owners)}" : $"{verb} {files} files";
    }

    private static string Verb(CodeChangeKind kind) => kind switch
    {
        CodeChangeKind.Added => "add",
        CodeChangeKind.Removed => "remove",
        _ => "update",
    };

    /// <summary>«Cache.LoadAsync(string, int)» → «Cache.LoadAsync»: в сообщении параметры не нужны.</summary>
    private static string Short(string name)
    {
        var paren = name.IndexOf('(');
        return paren < 0 ? name : name[..paren];
    }

    /// <summary>Тип, которому принадлежит член: «Outer.Inner.M()» → «Inner»; сам тип — он же.</summary>
    private static string Owner(string name)
    {
        var parts = Short(name).Split('.');
        return parts.Length == 1 ? parts[0] : parts[^2];
    }

    private static string Member(string name) => Short(name).Split('.')[^1];

    private static bool IsTest(string path) =>
        path.Contains("Test", StringComparison.Ordinal) || path.Contains("/tests/", StringComparison.OrdinalIgnoreCase);
}
