using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git status --porcelain=v2 --branch -z</c>.
/// Формат: https://git-scm.com/docs/git-status#_porcelain_format_version_2
/// </summary>
public static class StatusParser
{
    public static readonly string[] Arguments = ["status", "--porcelain=v2", "--branch", "-z"];

    public static RepoStatus Parse(string output)
    {
        string? branch = null;
        string? headHash = null;
        string? upstream = null;
        int ahead = 0, behind = 0;
        var files = new List<FileChange>();

        // С -z записи разделены символом NUL, а пути не экранируются.
        var records = output.Split('\0');
        for (var i = 0; i < records.Length; i++)
        {
            var record = records[i];
            if (record.Length == 0)
                continue;

            switch (record[0])
            {
                case '#':
                    ParseHeader(record, ref branch, ref headHash, ref upstream, ref ahead, ref behind);
                    break;

                case '1':
                {
                    // 1 XY sub mH mI mW hH hI path
                    var fields = record.Split(' ', 9);
                    files.Add(new FileChange(fields[8], ToKind(fields[1][0]), ToKind(fields[1][1])));
                    break;
                }

                case '2':
                {
                    // 2 XY sub mH mI mW hH hI Xscore path, следующая запись — исходный путь.
                    var fields = record.Split(' ', 10);
                    var originalPath = i + 1 < records.Length ? records[++i] : null;
                    files.Add(new FileChange(fields[9], ToKind(fields[1][0]), ToKind(fields[1][1]), originalPath));
                    break;
                }

                case 'u':
                {
                    // u XY sub m1 m2 m3 mW h1 h2 h3 path
                    var fields = record.Split(' ', 11);
                    files.Add(new FileChange(fields[10], ChangeKind.Unmerged, ChangeKind.Unmerged));
                    break;
                }

                case '?':
                    files.Add(new FileChange(record[2..], ChangeKind.Unmodified, ChangeKind.Untracked));
                    break;
            }
        }

        return new RepoStatus(branch, headHash, upstream, ahead, behind, files);
    }

    private static void ParseHeader(
        string record,
        ref string? branch,
        ref string? headHash,
        ref string? upstream,
        ref int ahead,
        ref int behind)
    {
        // # <имя> <значение>
        var parts = record.Split(' ', 3);
        if (parts.Length < 3)
            return;

        var value = parts[2];
        switch (parts[1])
        {
            case "branch.oid":
                headHash = value == "(initial)" ? null : value;
                break;
            case "branch.head":
                branch = value == "(detached)" ? null : value;
                break;
            case "branch.upstream":
                upstream = value;
                break;
            case "branch.ab":
                // +<ahead> -<behind>
                var counts = value.Split(' ');
                ahead = int.Parse(counts[0].TrimStart('+'));
                behind = int.Parse(counts[1].TrimStart('-'));
                break;
        }
    }

    private static ChangeKind ToKind(char code) => code switch
    {
        'M' => ChangeKind.Modified,
        'A' => ChangeKind.Added,
        'D' => ChangeKind.Deleted,
        'R' => ChangeKind.Renamed,
        'C' => ChangeKind.Copied,
        'T' => ChangeKind.TypeChanged,
        'U' => ChangeKind.Unmerged,
        _ => ChangeKind.Unmodified,
    };
}
