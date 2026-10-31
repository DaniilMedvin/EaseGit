using System.Globalization;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git log -z</c>. Коммиты разделены символом NUL,
/// поля — символом US (0x1F): хеш, имя автора, email, дата ISO 8601, заголовок.
/// </summary>
public static class LogParser
{
    private const char FieldSeparator = '\x1f';

    public static string[] Arguments(int limit) =>
    [
        "log",
        "-z",
        $"-n{limit}",
        "--format=%H%x1f%an%x1f%ae%x1f%aI%x1f%s",
    ];

    public static IReadOnlyList<Commit> Parse(string output)
    {
        var commits = new List<Commit>();
        foreach (var record in output.Split('\0'))
        {
            var fields = record.Split(FieldSeparator);
            if (fields.Length < 5)
                continue;

            commits.Add(new Commit(
                Hash: fields[0].Trim(),
                AuthorName: fields[1],
                AuthorEmail: fields[2],
                Date: DateTimeOffset.Parse(fields[3], CultureInfo.InvariantCulture),
                Subject: fields[4]));
        }

        return commits;
    }
}
