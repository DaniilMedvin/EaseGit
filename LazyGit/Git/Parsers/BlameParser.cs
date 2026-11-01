using System.Globalization;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор <c>git blame --porcelain</c>: у каждой строки шапка «хеш исходная_строка итоговая_строка [число]»,
/// у первой строки коммита — поля author, author-time, summary, filename, previous…, затем «\tтекст».
/// </summary>
public static class BlameParser
{
    /// <param name="revision">Состояние файла в этом коммите; null — рабочая копия (с незакоммиченными строками).</param>
    public static string[] Arguments(string path, string? revision = null) =>
        revision is null ? ["blame", "--porcelain", "--", path] : ["blame", "--porcelain", revision, "--", path];

    public static IReadOnlyList<BlameLine> Parse(string output)
    {
        var lines = new List<BlameLine>();
        var commits = new Dictionary<string, Dictionary<string, string>>();
        string? hash = null;
        var number = 0;
        foreach (var raw in output.Split('\n'))
        {
            if (raw.StartsWith('\t'))
            {
                if (hash is not null)
                    lines.Add(new BlameLine(number, raw[1..].TrimEnd('\r'), Build(hash, commits[hash])));
                continue;
            }

            var space = raw.IndexOf(' ');
            var key = space < 0 ? raw : raw[..space];
            if (key.Length == 40 && key.All(Uri.IsHexDigit))
            {
                hash = key;
                var parts = raw.Split(' ');
                number = parts.Length > 2 && int.TryParse(parts[2], out var final) ? final : number + 1;
                commits.TryAdd(hash, []);
            }
            else if (hash is not null && key.Length > 0)
            {
                commits[hash][key] = space < 0 ? "" : raw[(space + 1)..];
            }
        }

        return lines;
    }

    private static BlameCommit Build(string hash, Dictionary<string, string> fields)
    {
        var seconds = long.TryParse(fields.GetValueOrDefault("author-time"), out var time) ? time : 0;
        var offset = ParseOffset(fields.GetValueOrDefault("author-tz"));
        var previous = fields.GetValueOrDefault("previous")?.Split(' ', 2);
        return new BlameCommit(
            hash,
            fields.GetValueOrDefault("author") ?? "",
            (fields.GetValueOrDefault("author-mail") ?? "").Trim('<', '>'),
            DateTimeOffset.FromUnixTimeSeconds(seconds).ToOffset(offset),
            fields.GetValueOrDefault("summary") ?? "",
            fields.GetValueOrDefault("filename") ?? "",
            previous is [var previousHash, ..] ? previousHash : null,
            previous is [_, var previousPath] ? previousPath : null);
    }

    /// <summary>«+0300» → 3 часа.</summary>
    private static TimeSpan ParseOffset(string? tz)
    {
        if (tz is not { Length: 5 } || !int.TryParse(tz[1..3], out var hours) || !int.TryParse(tz[3..], out var minutes))
            return TimeSpan.Zero;

        var offset = new TimeSpan(hours, minutes, 0);
        return tz[0] == '-' ? -offset : offset;
    }
}
