using System.Text;
using LazyGit.Models;

namespace LazyGit.Highlighting;

/// <summary>
/// Подсветка синтаксиса строк diff. Каждый фрагмент собирается в две версии кода —
/// новую (контекст и добавленные строки) и старую (контекст и удалённые строки),
/// версии подсвечиваются целиком, затем результат раскладывается по строкам.
/// </summary>
public static class DiffHighlighter
{
    private static readonly HashSet<string> CSharpExtensions = new(StringComparer.OrdinalIgnoreCase) { ".cs", ".csx" };

    private static readonly HashSet<string> XmlExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".csproj", ".fsproj", ".vbproj", ".props", ".targets", ".xml", ".xaml", ".axaml", ".resx", ".config", ".nuspec",
        ".manifest", ".slnx", ".xsd", ".runsettings", ".ruleset", ".pubxml", ".svg",
    };

    private static readonly HashSet<string> RazorExtensions = new(StringComparer.OrdinalIgnoreCase) { ".razor", ".cshtml" };

    private static readonly HashSet<string> JsonExtensions = new(StringComparer.OrdinalIgnoreCase) { ".json", ".jsonc" };

    /// <summary>C#-файл: для него есть и подсветка Roslyn, и сводка изменённых классов и методов.</summary>
    public static bool IsCSharp(string path) => CSharpExtensions.Contains(Path.GetExtension(path));

    /// <summary>Есть ли подсветка для файла: C#, XML-разметка (проекты, XAML, ресурсы), Razor, JSON.</summary>
    public static bool IsSupported(string path) => Highlighter(path) is not null;

    private static Func<string, IReadOnlyList<HighlightSpan>>? Highlighter(string path)
    {
        var extension = Path.GetExtension(path);
        if (CSharpExtensions.Contains(extension))
            return CSharpHighlighter.Highlight;
        if (XmlExtensions.Contains(extension))
            return text => MarkupHighlighter.Highlight(text, razor: false);
        if (RazorExtensions.Contains(extension))
            return text => MarkupHighlighter.Highlight(text, razor: true);
        return JsonExtensions.Contains(extension) ? JsonHighlighter.Highlight : null;
    }

    /// <summary>
    /// Spans для каждой строки diff (позиции относительно <see cref="DiffLine.Text"/>).
    /// null — строка не подсвечивается (служебная строка или язык не поддерживается).
    /// </summary>
    public static IReadOnlyList<HighlightSpan>?[] Highlight(IReadOnlyList<DiffLine> lines, string path)
    {
        var result = new IReadOnlyList<HighlightSpan>?[lines.Count];
        if (Highlighter(path) is not { } highlighter)
            return result;

        var start = 0;
        while (start < lines.Count)
        {
            if (!IsBody(lines[start].Kind))
            {
                start++;
                continue;
            }

            var end = start;
            while (end < lines.Count && (IsBody(lines[end].Kind) || lines[end].Kind == DiffLineKind.NoNewline))
                end++;

            HighlightSide(lines, start, end, result, highlighter,
                include: k => k is DiffLineKind.Context or DiffLineKind.Added,
                assign: k => k is DiffLineKind.Context or DiffLineKind.Added);
            HighlightSide(lines, start, end, result, highlighter,
                include: k => k is DiffLineKind.Context or DiffLineKind.Removed,
                assign: k => k is DiffLineKind.Removed);
            start = end;
        }

        return result;
    }

    private static bool IsBody(DiffLineKind kind) =>
        kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed;

    private static void HighlightSide(
        IReadOnlyList<DiffLine> lines,
        int start,
        int end,
        IReadOnlyList<HighlightSpan>?[] result,
        Func<string, IReadOnlyList<HighlightSpan>> highlighter,
        Func<DiffLineKind, bool> include,
        Func<DiffLineKind, bool> assign)
    {
        var text = new StringBuilder();
        var included = new List<(int Index, int Offset)>();
        for (var i = start; i < end; i++)
        {
            if (!include(lines[i].Kind))
                continue;

            included.Add((i, text.Length));
            text.Append(lines[i].Text).Append('\n');
        }

        if (included.Count == 0)
            return;

        var spans = highlighter(text.ToString());
        var next = 0;
        foreach (var (index, offset) in included)
        {
            var lineEnd = offset + lines[index].Text.Length;
            var lineSpans = new List<HighlightSpan>();

            // Spans отсортированы; многострочный span (комментарий, строка) режется по границам строк.
            while (next < spans.Count && spans[next].End <= offset)
                next++;
            for (var j = next; j < spans.Count && spans[j].Start < lineEnd; j++)
            {
                var from = Math.Max(spans[j].Start, offset);
                var to = Math.Min(spans[j].End, lineEnd);
                if (to > from)
                    lineSpans.Add(new HighlightSpan(from - offset, to - from, spans[j].Class));
            }

            if (assign(lines[index].Kind))
                result[index] = lineSpans;
        }
    }
}
