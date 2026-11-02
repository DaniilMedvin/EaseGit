using EaseGit.Highlighting;
using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Blame: «a1b2c3d  автор        2 нед │  12 │ код». Подпись коммита — только у первой из подряд идущих строк одного
/// коммита, остальные связаны с ней линией «│» под хешем («└» у последней); строки того же коммита, что и выбранная,
/// отмечены цветом accent. Код подсвечивается, как в diff.
/// </summary>
public sealed class BlamePanel : ListPanel<BlameLine>
{
    private const int AuthorWidth = 12;

    private IReadOnlyList<HighlightSpan>?[] _highlights = [];
    private int _numberWidth = 3;

    protected override string EmptyText => T("Файл пуст", "The file is empty");

    protected override string KeyOf(BlameLine item) => item.Number.ToString();

    /// <summary>Строки файла; <paramref name="path"/> — для подсветки по расширению.</summary>
    public void SetLines(IReadOnlyList<BlameLine> lines, string path)
    {
        _highlights = DiffHighlighter.Highlight(lines.Select(l => new DiffLine(DiffLineKind.Context, l.Text.Replace("\t", "    "))).ToList(), path);
        _numberWidth = Math.Max(3, lines.Count.ToString().Length);
        SetItems(lines);
    }

    protected override void DrawItem(int line, BlameLine blame, bool selected, Color? background)
    {
        var index = blame.Number - 1;
        var commit = blame.Commit;
        var first = index == 0 || index >= Items.Count || Items[index - 1].Commit.Hash != commit.Hash;
        var same = Selected is { } current && current.Commit.Hash == commit.Hash;

        var segments = new List<Segment> { Marker(selected) };
        if (first)
        {
            var author = commit.IsUncommitted ? T("не закоммичено", "uncommitted") : commit.Author;
            author = author.Length > AuthorWidth ? author[..(AuthorWidth - 1)] + Theme.SymbolEllipsis : author;
            var age = commit.IsUncommitted ? "" : RelativeTime.Format(commit.Date, DateTimeOffset.Now);
            segments.Add(new Segment(commit.IsUncommitted ? "       " : commit.ShortHash, same ? Theme.Accent : Theme.Warning, same ? TextStyle.Bold : TextStyle.None));
            segments.Add(new Segment($" {author,-AuthorWidth} {age,6}", same ? Theme.Fg : Theme.Muted));
        }
        else
        {
            // Продолжение того же коммита: линия под хешем связывает строки, «└» — последняя строка группы.
            var last = index + 1 >= Items.Count || Items[index + 1].Commit.Hash != commit.Hash;
            segments.Add(new Segment(last ? "└" : "│", same ? Theme.Accent : Theme.Dim));
            segments.Add(new Segment(new string(' ', 7 + AuthorWidth + 1 + 6), Theme.Dim));
        }

        segments.Add(new Segment(" │", same ? Theme.Accent : Theme.Dim));
        segments.Add(new Segment(blame.Number.ToString().PadLeft(_numberWidth) + " │ ", Theme.Dim));
        segments.AddRange(Code(blame.Text.Replace("\t", "    "), index < _highlights.Length ? _highlights[index] : null));
        this.DrawSegments(1, line, segments, Viewport.Width - 2, background);
    }

    private static IEnumerable<Segment> Code(string text, IReadOnlyList<HighlightSpan>? spans)
    {
        if (spans is null)
        {
            yield return new Segment(text, Theme.Fg);
            yield break;
        }

        var position = 0;
        foreach (var span in spans)
        {
            if (span.Start >= text.Length)
                break;
            if (span.Start > position)
                yield return new Segment(text[position..span.Start], Theme.SyntaxPlain);
            var end = Math.Min(span.Start + span.Length, text.Length);
            yield return new Segment(text[span.Start..end], Theme.Syntax(span.Class));
            position = end;
        }

        if (position < text.Length)
            yield return new Segment(text[position..], Theme.SyntaxPlain);
    }
}
