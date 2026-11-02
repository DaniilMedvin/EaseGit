using System.Text;
using System.Text.RegularExpressions;
using EaseGit.Models;

namespace EaseGit.Git;

/// <summary>
/// Патч из выбранных строк diff одного файла для построчного stage: <c>git apply --cached</c> применяет его к индексу.
/// </summary>
public static partial class PatchBuilder
{
    /// <summary>
    /// Строит патч, в который входят только выбранные добавленные и удалённые строки.
    /// Без <paramref name="reverse"/> — для stage из diff «индекс → рабочая копия»: невыбранное «+» выбрасывается,
    /// невыбранное «-» становится контекстом (в индексе эта строка остаётся).
    /// С <paramref name="reverse"/> — для unstage из diff «HEAD → индекс», патч применяется с --reverse:
    /// невыбранное «-» выбрасывается, невыбранное «+» становится контекстом.
    /// </summary>
    /// <param name="lines">Diff одного файла из <see cref="Parsers.DiffParser"/>, вместе со служебными строками.</param>
    /// <param name="selected">Индексы выбранных строк в <paramref name="lines"/>.</param>
    /// <returns>Текст патча или null, если среди выбранных нет изменений.</returns>
    public static string? Build(IReadOnlyList<DiffLine> lines, IReadOnlySet<int> selected, bool reverse)
    {
        var firstHunk = -1;
        for (var i = 0; i < lines.Count && firstHunk < 0; i++)
            if (lines[i].Kind == DiffLineKind.Hunk)
                firstHunk = i;
        if (firstHunk < 0)
            return null;

        var body = new StringBuilder();
        var delta = 0;
        bool emitted = false, preImage = false, postImage = false;
        var hunkStart = firstHunk;
        while (hunkStart < lines.Count)
        {
            var hunkEnd = hunkStart + 1;
            while (hunkEnd < lines.Count && lines[hunkEnd].Kind is not (DiffLineKind.Hunk or DiffLineKind.Header))
                hunkEnd++;

            var hunk = BuildHunk(lines, hunkStart, hunkEnd, selected, reverse, ref delta);
            if (hunk is { } h)
            {
                body.Append(h.Text);
                emitted = true;
                preImage |= h.OldCount > 0;
                postImage |= h.NewCount > 0;
            }

            if (hunkEnd < lines.Count && lines[hunkEnd].Kind == DiffLineKind.Header)
                break;
            hunkStart = hunkEnd;
        }

        if (!emitted)
            return null;

        var header = FileHeader(lines.Take(firstHunk).Where(l => l.Kind == DiffLineKind.Header).Select(l => l.Text).ToList(),
            preImage, postImage);
        return string.Join("\n", header) + "\n" + body;
    }

    private readonly record struct Hunk(string Text, int OldCount, int NewCount);

    private static Hunk? BuildHunk(IReadOnlyList<DiffLine> lines, int start, int end, IReadOnlySet<int> selected, bool reverse,
        ref int delta)
    {
        var (oldStart, newStart) = ParseRange(lines[start].Text);
        var output = new StringBuilder();
        int oldCount = 0, newCount = 0;
        var changed = false;
        var lastKept = false;
        for (var i = start + 1; i < end; i++)
        {
            var line = lines[i];
            var prefix = line.Kind switch
            {
                DiffLineKind.Context => ' ',
                DiffLineKind.Added when selected.Contains(i) => '+',
                DiffLineKind.Removed when selected.Contains(i) => '-',
                // Невыбранная строка той стороны, что останется в индексе, — контекст, другой стороны — выбрасывается.
                DiffLineKind.Added => reverse ? ' ' : '\0',
                DiffLineKind.Removed => reverse ? '\0' : ' ',
                _ => '\0',
            };

            if (line.Kind == DiffLineKind.NoNewline)
            {
                // «\ No newline» относится к предыдущей строке и нужен, только если она осталась в патче.
                if (lastKept)
                    output.Append(line.Text).Append('\n');
                continue;
            }

            lastKept = prefix != '\0';
            if (!lastKept)
                continue;

            changed |= prefix != ' ';
            if (prefix != '+')
                oldCount++;
            if (prefix != '-')
                newCount++;
            output.Append(prefix).Append(line.Text).Append(line.EndsWithCr ? "\r\n" : "\n");
        }

        if (!changed)
            return null;

        // Сторона, к которой применяется патч, не меняется (все её строки на месте), поэтому её начало берётся из заголовка.
        // Начало другой стороны сдвигается на разницу длин уже вошедших фрагментов. При нулевой длине git пишет
        // номер строки, после которой идёт фрагмент, — отсюда ±1.
        int oldOut, newOut;
        if (reverse)
        {
            newOut = newStart;
            oldOut = newStart + delta + (newCount == 0 ? 1 : 0) - (oldCount == 0 ? 1 : 0);
            delta += oldCount - newCount;
        }
        else
        {
            oldOut = oldStart;
            newOut = oldStart + delta + (oldCount == 0 ? 1 : 0) - (newCount == 0 ? 1 : 0);
            delta += newCount - oldCount;
        }

        return new Hunk($"@@ -{oldOut},{oldCount} +{newOut},{newCount} @@\n{output}", oldCount, newCount);
    }

    /// <summary>
    /// Служебные строки патча. Новый файл (--- /dev/null), если в патч попали не все строки и до него остаётся
    /// содержимое, превращается в изменение файла; так же удалённый (+++ /dev/null). Строки index не нужны git apply.
    /// </summary>
    private static List<string> FileHeader(List<string> header, bool preImage, bool postImage)
    {
        var oldPath = header.FirstOrDefault(l => l.StartsWith("--- a/"))?.TrimEnd('\t')[6..];
        var newPath = header.FirstOrDefault(l => l.StartsWith("+++ b/"))?.TrimEnd('\t')[6..];
        var result = new List<string>();
        var undoCreation = preImage && header.Contains("--- /dev/null");
        var undoDeletion = postImage && header.Contains("+++ /dev/null");
        foreach (var line in header)
        {
            if (line.StartsWith("index "))
                continue;
            if (undoCreation && line.StartsWith("new file mode"))
                continue;
            if (undoDeletion && line.StartsWith("deleted file mode"))
                continue;

            if (undoCreation && line == "--- /dev/null")
                result.Add("--- a/" + newPath);
            else if (undoDeletion && line == "+++ /dev/null")
                result.Add("+++ b/" + oldPath);
            else
                result.Add(line);
        }

        return result;
    }

    private static (int Old, int New) ParseRange(string header)
    {
        var match = RangeRegex().Match(header);
        return match.Success ? (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value)) : (0, 0);
    }

    [GeneratedRegex(@"^@@ -(\d+)(?:,\d+)? \+(\d+)(?:,\d+)? @@")]
    private static partial Regex RangeRegex();
}
