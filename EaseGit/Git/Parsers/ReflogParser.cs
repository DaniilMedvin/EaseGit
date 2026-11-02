using EaseGit.Models;

namespace EaseGit.Git.Parsers;

/// <summary>Разбор <c>git log -g -z</c>: записи разделены NUL, поля — символом US (0x1F): хеш, описание.</summary>
public static class ReflogParser
{
    /// <summary>Последние <paramref name="count"/> записей reflog HEAD, новые первыми.</summary>
    public static string[] Arguments(int count) => ["log", "--walk-reflogs", "-z", $"-n{count}", "--format=%H%x1f%gs", "HEAD"];

    public static IReadOnlyList<ReflogEntry> Parse(string output) =>
        output.Split('\0')
            .Select(record => record.Trim('\n').Split('\x1f'))
            .Where(fields => fields.Length >= 2 && fields[0].Length > 0)
            .Select(fields => new ReflogEntry(fields[0], fields[1]))
            .ToList();
}
