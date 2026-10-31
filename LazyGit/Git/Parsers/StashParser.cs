using System.Globalization;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор <c>git stash list -z</c>: записи разделены NUL, поля — символом US (0x1F):
/// ссылка, хеш, имя и email автора, дата, описание («On main: msg» или «WIP on main: abc init»).
/// </summary>
public static class StashParser
{
    public static readonly string[] Arguments =
        ["stash", "list", "-z", "--format=%gd%x1f%H%x1f%an%x1f%ae%x1f%aI%x1f%gs"];

    public static IReadOnlyList<StashEntry> Parse(string output)
    {
        var entries = new List<StashEntry>();
        foreach (var record in output.Split('\0'))
        {
            var fields = record.Trim('\n').Split('\x1f');
            if (fields.Length < 6)
                continue;

            var (message, branch) = SplitSubject(fields[5]);
            entries.Add(new StashEntry(fields[0], fields[1], fields[2], fields[3],
                DateTimeOffset.Parse(fields[4], CultureInfo.InvariantCulture), message, branch));
        }

        return entries;
    }

    /// <summary>«On main: my message» → («my message», «main»); у WIP-записи сообщение остаётся целиком.</summary>
    private static (string Message, string? Branch) SplitSubject(string subject)
    {
        var colon = subject.IndexOf(": ", StringComparison.Ordinal);
        if (colon < 0)
            return (subject, null);

        var head = subject[..colon];
        if (head.StartsWith("On "))
            return (subject[(colon + 2)..], head[3..]);
        if (head.StartsWith("WIP on "))
            return (subject, head[7..]);
        return (subject, null);
    }
}
