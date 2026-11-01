using LazyGit.Highlighting;
using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Panels;

/// <summary>
/// Файл с конфликтом целиком: обычный текст и блоки, где у каждой стороны свой заголовок и цветная полоса слева —
/// наша (info), база (dim, если есть), их (warning). Текущий блок яркий, остальные приглушены.
/// ↑↓ / j k — к соседнему блоку, колесо и <see cref="ScrollBy"/> листают. Код подсвечивается, как в diff.
/// </summary>
public sealed class ConflictPanel : View
{
    private const int TabWidth = 4;

    private enum RowKind
    {
        Text,
        Header,
        Ours,
        Base,
        Theirs,
        End,
    }

    /// <param name="First">Первый заголовок блока: на нём «❯» у текущего блока.</param>
    private sealed record Row(RowKind Kind, string Text, int Block, IReadOnlyList<HighlightSpan>? Highlight = null, bool First = false);

    private IReadOnlyList<Row> _rows = [];
    private int _blocks;
    private int _current;
    private int _scroll;

    public ConflictPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    /// <summary>Номер текущего блока с нуля.</summary>
    public int CurrentBlock => _current;

    public int Blocks => _blocks;

    /// <summary>Показывает файл. Текущий блок остаётся на том же номере: после решения им становится следующий.</summary>
    public void SetFile(ConflictFile file)
    {
        var rows = new List<Row>();
        var block = 0;
        foreach (var part in file.Parts)
        {
            if (part is PlainPart plain)
            {
                rows.AddRange(plain.Lines.Select(l => new Row(RowKind.Text, Clean(l), -1)));
                continue;
            }

            var conflict = (ConflictBlock)part;
            rows.Add(new Row(RowKind.Header, Side(T("наша", "ours"), conflict.OursLabel), block, First: true));
            rows.AddRange(conflict.Ours.Select(l => new Row(RowKind.Ours, Clean(l), block)));
            if (conflict.Base is { } @base)
            {
                rows.Add(new Row(RowKind.Header, Side(T("база", "base"), conflict.BaseMarker![7..].Trim()), block));
                rows.AddRange(@base.Select(l => new Row(RowKind.Base, Clean(l), block)));
            }

            rows.Add(new Row(RowKind.Header, Side(T("их", "theirs"), conflict.TheirsLabel), block));
            rows.AddRange(conflict.Theirs.Select(l => new Row(RowKind.Theirs, Clean(l), block)));
            rows.Add(new Row(RowKind.End, "", block));
            block++;
        }

        // Подсветка — по всем строкам кода сразу (заголовки и концы блоков — пустыми строками), как контекст diff.
        var highlights = DiffHighlighter.Highlight(
            rows.Select(r => new DiffLine(DiffLineKind.Context, r.Kind is RowKind.Header or RowKind.End ? "" : r.Text)).ToList(), file.Path);
        _rows = rows.Select((r, i) => r with { Highlight = r.Kind is RowKind.Header or RowKind.End ? null : highlights[i] }).ToList();
        _blocks = block;
        _current = Math.Clamp(_current, 0, Math.Max(0, block - 1));
        ShowCurrent();
    }

    /// <summary>Сбрасывает текущий блок на первый: для нового файла.</summary>
    public void Reset()
    {
        _current = 0;
        _scroll = 0;
    }

    private static string Side(string side, string label) => label.Length == 0 ? side : $"{side} · {label}";

    private static string Clean(string line) => line.TrimEnd('\n').TrimEnd('\r').Replace("\t", new string(' ', TabWidth));

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.J)
            return Move(+1);
        if (key == Key.CursorUp || key == Key.K)
            return Move(-1);
        return base.OnKeyDown(key);
    }

    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            ScrollBy(ListPanel<object>.WheelStep);
        else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            ScrollBy(-ListPanel<object>.WheelStep);
        else
            return base.OnMouseEvent(mouse);
        return true;
    }

    private bool Move(int step)
    {
        var next = Math.Clamp(_current + step, 0, Math.Max(0, _blocks - 1));
        if (next != _current)
        {
            _current = next;
            ShowCurrent();
        }

        return true;
    }

    public void ScrollBy(int delta)
    {
        var scroll = Math.Clamp(_scroll + delta, 0, Math.Max(0, _rows.Count - Viewport.Height));
        if (scroll == _scroll)
            return;

        _scroll = scroll;
        SetNeedsDraw();
    }

    /// <summary>Прокручивает так, чтобы текущий блок был виден целиком (или с начала) и над ним — пара строк контекста.</summary>
    private void ShowCurrent()
    {
        var first = _rows.ToList().FindIndex(r => r.Block == _current);
        var last = _rows.ToList().FindLastIndex(r => r.Block == _current);
        var height = Math.Max(1, Viewport.Height);
        if (first >= 0 && (first < _scroll || last >= _scroll + height))
            _scroll = Math.Max(0, first - Math.Min(3, height / 4));
        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, _rows.Count - height));
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        for (var line = 0; line < Viewport.Height && _scroll + line < _rows.Count; line++)
            DrawRow(line, _rows[_scroll + line]);
        return true;
    }

    private void DrawRow(int line, Row row)
    {
        var width = Viewport.Width;
        var current = row.Block == _current;
        var (bar, color) = row.Kind switch
        {
            RowKind.Ours => ("▌", Theme.Info),
            RowKind.Base => ("▌", Theme.Muted),
            RowKind.Theirs => ("▌", Theme.Warning),
            RowKind.Header or RowKind.End => ("▌", current ? Theme.Accent : Theme.Dim),
            _ => (" ", Theme.Dim),
        };
        if (row.Block >= 0 && !current)
            color = Theme.Dim;

        var segments = new List<Segment> { new(bar + " ", color) };
        switch (row.Kind)
        {
            case RowKind.Header:
                var title = $"{T("Блок", "Block")} {row.Block + 1}/{_blocks} {Theme.SymbolSeparator} {row.Text}";
                segments.Add(new Segment((current && row.First ? Theme.SymbolSelection + " " : "") + title,
                    current ? Theme.Accent : Theme.Dim, current ? TextStyle.Bold : TextStyle.None));
                break;
            case RowKind.End:
                segments.Add(new Segment(new string('─', Math.Max(0, Math.Min(20, width - 3))), color));
                break;
            default:
                var dim = row.Block >= 0 && !current;
                segments.AddRange(CodeSegments(row, dim));
                break;
        }

        this.DrawSegments(0, line, segments, width);
    }

    /// <summary>Код с подсветкой; в приглушённом блоке — одним цветом, чтобы текущий выделялся.</summary>
    private static IEnumerable<Segment> CodeSegments(Row row, bool dim)
    {
        if (dim || row.Highlight is null)
        {
            yield return new Segment(row.Text, dim ? Theme.Muted : Theme.Fg);
            yield break;
        }

        var position = 0;
        foreach (var span in row.Highlight)
        {
            if (span.Start > position)
                yield return new Segment(row.Text[position..Math.Min(span.Start, row.Text.Length)], Theme.SyntaxPlain);
            if (span.Start >= row.Text.Length)
                break;
            var end = Math.Min(span.Start + span.Length, row.Text.Length);
            yield return new Segment(row.Text[span.Start..end], Theme.Syntax(span.Class));
            position = end;
        }

        if (position < row.Text.Length)
            yield return new Segment(row.Text[position..], Theme.SyntaxPlain);
    }
}
