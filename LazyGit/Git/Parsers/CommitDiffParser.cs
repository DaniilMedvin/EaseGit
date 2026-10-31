using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git show --format=%B%x00</c>: сообщение коммита, символ NUL, затем diff.
/// Для merge-коммита diff строится относительно первого родителя — так видно, что принесла ветка.
/// </summary>
public static class CommitDiffParser
{
    public static string[] Arguments(string hash) =>
    [
        "show",
        "--no-color",
        "--no-ext-diff",
        "--find-renames",
        "--diff-merges=first-parent",
        "--format=%B%x00",
        hash,
    ];

    public static CommitDiff Parse(string output)
    {
        var separator = output.IndexOf('\0');
        if (separator < 0)
            return new CommitDiff(output.TrimEnd(), []);

        var message = output[..separator].TrimEnd();
        return new CommitDiff(message, DiffParser.Parse(output[(separator + 1)..].TrimStart('\n')));
    }
}
