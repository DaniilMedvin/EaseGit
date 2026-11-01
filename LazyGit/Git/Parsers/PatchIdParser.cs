namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор <c>git patch-id --stable</c>: строки «отпечаток коммит». Отпечаток не зависит от хеша, родителя и сообщения —
/// по нему находится копия коммита после rebase или squash.
/// </summary>
public static class PatchIdParser
{
    public static readonly string[] Arguments = ["patch-id", "--stable"];

    /// <summary>Пары «отпечаток → коммит» в порядке вывода (у diff без коммита — нулевой хеш).</summary>
    public static IReadOnlyList<(string PatchId, string Commit)> Parse(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim().Split(' '))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0], parts[1]))
            .ToList();
}
