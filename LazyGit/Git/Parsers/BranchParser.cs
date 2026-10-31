using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git for-each-ref</c> для локальных веток.
/// Одна строка на ветку, поля разделены символом NUL: HEAD, имя, upstream, состояние upstream.
/// </summary>
public static class BranchParser
{
    public static readonly string[] Arguments =
    [
        "for-each-ref",
        "--format=%(HEAD)%00%(refname:short)%00%(upstream:short)%00%(upstream:track,nobracket)",
        "refs/heads",
    ];

    public static IReadOnlyList<Branch> Parse(string output)
    {
        var branches = new List<Branch>();
        foreach (var line in output.Split('\n'))
        {
            var fields = line.Split('\0');
            if (fields.Length < 4)
                continue;

            var (ahead, behind, gone) = ParseTrack(fields[3]);
            branches.Add(new Branch(
                Name: fields[1],
                IsCurrent: fields[0] == "*",
                Upstream: fields[2].Length > 0 ? fields[2] : null,
                Ahead: ahead,
                Behind: behind,
                IsUpstreamGone: gone));
        }

        return branches;
    }

    /// <summary>Разбирает «ahead 1», «behind 2», «ahead 1, behind 2» или «gone».</summary>
    private static (int Ahead, int Behind, bool Gone) ParseTrack(string track)
    {
        if (track == "gone")
            return (0, 0, true);

        int ahead = 0, behind = 0;
        foreach (var part in track.Split(", ", StringSplitOptions.RemoveEmptyEntries))
        {
            var pieces = part.Split(' ');
            if (pieces.Length != 2 || !int.TryParse(pieces[1], out var count))
                continue;

            if (pieces[0] == "ahead")
                ahead = count;
            else if (pieces[0] == "behind")
                behind = count;
        }

        return (ahead, behind, false);
    }
}
