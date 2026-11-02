using System.Globalization;
using EaseGit.Models;

namespace EaseGit.Git.Parsers;

/// <summary>
/// Разбор <c>git log --follow -z --name-status</c>: история файла через переименования. Запись коммита начинается
/// с RS (0x1E), поля — через US (0x1F), затем NUL и строки статуса «M\0путь\0» или «R100\0старый\0новый\0».
/// </summary>
public static class FileHistoryParser
{
    private const char RecordSeparator = '\x1e';
    private const char FieldSeparator = '\x1f';

    public static string[] Arguments(string path, int limit) =>
    [
        "log",
        "--follow",
        "-z",
        $"-n{limit}",
        "--name-status",
        "--format=%x1e%H%x1f%an%x1f%ae%x1f%aI%x1f%s%x1f%P",
        "--",
        path,
    ];

    public static IReadOnlyList<FileRevision> Parse(string output)
    {
        var revisions = new List<FileRevision>();
        foreach (var record in output.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var items = record.Split('\0');
            var fields = items[0].Split(FieldSeparator);
            if (fields.Length < 6)
                continue;

            var commit = new Commit(fields[0], fields[1], fields[2], DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture), fields[4])
            {
                Parents = fields[5].Split(' ', StringSplitOptions.RemoveEmptyEntries),
            };

            // После шапки: «\n», статус, путь(и). Статус переименования — «R100», у остальных — одна буква.
            var status = items.Skip(1).Select(i => i.Trim('\n')).Where(i => i.Length > 0).ToList();
            if (status.Count < 2)
                continue;

            var kind = status[0][0];
            revisions.Add(kind is 'R' or 'C' && status.Count >= 3
                ? new FileRevision(commit, kind, status[2], status[1])
                : new FileRevision(commit, kind, status[1]));
        }

        return revisions;
    }
}
