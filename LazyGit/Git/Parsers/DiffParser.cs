using System.Text.RegularExpressions;
using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Разбор вывода <c>git diff</c> в формате unified, в том числе combined diff (<c>diff --cc</c>) для конфликтов.
/// </summary>
public static partial class DiffParser
{
    public static IReadOnlyList<DiffLine> Parse(string output)
    {
        var lines = new List<DiffLine>();
        var inHunk = false;
        var prefixWidth = 1;
        int oldNumber = 0, newNumber = 0;

        var rawLines = output.Split('\n');
        var count = rawLines.Length > 0 && rawLines[^1].Length == 0 ? rawLines.Length - 1 : rawLines.Length;
        for (var i = 0; i < count; i++)
        {
            var line = rawLines[i];
            var cr = line.EndsWith('\r');
            if (cr)
                line = line[..^1];

            if (line.StartsWith("diff --git ") || line.StartsWith("diff --cc ") || line.StartsWith("diff --combined "))
            {
                inHunk = false;
                lines.Add(new DiffLine(DiffLineKind.Header, line));
                continue;
            }

            if (line.StartsWith("@@"))
            {
                inHunk = true;
                // В combined diff число «@» равно количеству родителей + 1, префикс строк — на один символ меньше.
                prefixWidth = line.TakeWhile(c => c == '@').Count() - 1;
                (oldNumber, newNumber) = ParseHunkStart(line);
                lines.Add(new DiffLine(DiffLineKind.Hunk, line));
                continue;
            }

            if (!inHunk)
            {
                var kind = line.StartsWith("Binary files ") ? DiffLineKind.Binary : DiffLineKind.Header;
                lines.Add(new DiffLine(kind, line));
                continue;
            }

            if (line.StartsWith('\\'))
            {
                lines.Add(new DiffLine(DiffLineKind.NoNewline, line));
                continue;
            }

            var prefix = line.Length >= prefixWidth ? line[..prefixWidth] : line.PadRight(prefixWidth);
            var text = line.Length >= prefixWidth ? line[prefixWidth..] : "";
            if (prefix.Contains('-'))
            {
                lines.Add(new DiffLine(DiffLineKind.Removed, text, OldNumber: oldNumber++) { EndsWithCr = cr });
            }
            else if (prefix.Contains('+'))
            {
                lines.Add(new DiffLine(DiffLineKind.Added, text, NewNumber: newNumber++) { EndsWithCr = cr });
            }
            else
            {
                lines.Add(new DiffLine(DiffLineKind.Context, text, oldNumber++, newNumber++) { EndsWithCr = cr });
            }
        }

        return lines;
    }

    /// <summary>Начальные номера строк из «@@ -a,b +c,d @@» или «@@@ -a,b -c,d +e,f @@@».</summary>
    private static (int Old, int New) ParseHunkStart(string header)
    {
        var oldMatch = OldRangeRegex().Match(header);
        var newMatch = NewRangeRegex().Match(header);
        var oldStart = oldMatch.Success ? int.Parse(oldMatch.Groups[1].Value) : 0;
        var newStart = newMatch.Success ? int.Parse(newMatch.Groups[1].Value) : 0;
        return (oldStart, newStart);
    }

    [GeneratedRegex(@" -(\d+)")]
    private static partial Regex OldRangeRegex();

    [GeneratedRegex(@" \+(\d+)")]
    private static partial Regex NewRangeRegex();
}
