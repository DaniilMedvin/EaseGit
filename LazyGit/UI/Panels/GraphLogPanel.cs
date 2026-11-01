using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Panels;

/// <summary>
/// git log --graph в цветах приложения, как превью ветки в lazygit: граф линиями «│ ╱ ╲», коммит «●» (merge — «◉»)
/// цветом своей дорожки, жёлтый хеш, ссылки «(HEAD → main, origin/main, tag: v1.0)», автор и дата, тело сообщения.
/// Только просмотр: колесо мыши и <see cref="ScrollBy"/> листают.
/// </summary>
public sealed class GraphLogPanel : View
{
    private IReadOnlyList<GraphLogLine> _lines = [];
    private string? _message;
    private int _scroll;

    public GraphLogPanel()
    {
        CanFocus = false;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    /// <param name="keepScroll">Тот же лог после обновления экрана — прокрутка остаётся.</param>
    public void SetLines(IReadOnlyList<GraphLogLine> lines, bool keepScroll)
    {
        _lines = lines;
        _message = lines.Count == 0 ? T("Коммитов нет", "No commits") : null;
        _scroll = keepScroll ? Math.Clamp(_scroll, 0, MaxScroll) : 0;
        SetNeedsDraw();
    }

    public void SetMessage(string message)
    {
        _lines = [];
        _message = message;
        _scroll = 0;
        SetNeedsDraw();
    }

    /// <summary>Прокрутка на <paramref name="delta"/> строк; отрицательное значение — вверх.</summary>
    public void ScrollBy(int delta)
    {
        var scroll = Math.Clamp(_scroll + delta, 0, MaxScroll);
        if (scroll == _scroll)
            return;

        _scroll = scroll;
        SetNeedsDraw();
    }

    private int MaxScroll => Math.Max(0, _lines.Count - Viewport.Height);

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

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_message is not null)
        {
            this.DrawSegments(1, 0, [new Segment(_message, Theme.Muted)], Viewport.Width - 1);
            return true;
        }

        var now = DateTimeOffset.Now;
        for (var row = 0; row < Viewport.Height && _scroll + row < _lines.Count; row++)
        {
            var line = _lines[_scroll + row];
            var segments = GraphSegments(line).Concat(ContentSegments(line, now));
            this.DrawSegments(1, row, segments, Viewport.Width - 1);
        }

        return true;
    }

    /// <summary>Граф git линиями; цвет — по дорожке (колонке), как в панели коммитов.</summary>
    private static IEnumerable<Segment> GraphSegments(GraphLogLine line)
    {
        for (var i = 0; i < line.Graph.Length; i++)
        {
            var ch = line.Graph[i];
            var color = CommitsPanel.BranchColor(i / 2);
            yield return ch switch
            {
                '*' => new Segment(line.IsMerge ? "◉" : "●", color, TextStyle.Bold),
                '|' => new Segment("│", color),
                '/' => new Segment("╱", color),
                '\\' => new Segment("╲", color),
                '_' => new Segment("─", color),
                _ => new Segment(ch.ToString(), color),
            };
        }
    }

    private static IEnumerable<Segment> ContentSegments(GraphLogLine line, DateTimeOffset now)
    {
        switch (line.Kind)
        {
            case GraphLogLineKind.Commit:
                yield return new Segment(line.ShortHash ?? "", Theme.Warning);
                foreach (var segment in RefSegments(line.Refs))
                    yield return segment;
                yield return new Segment(" " + line.Text, Theme.Fg, TextStyle.Bold);
                break;
            case GraphLogLineKind.Author:
                var date = line.Date.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
                yield return new Segment(line.Text, Theme.Muted);
                yield return new Segment($" {Theme.SymbolSeparator} ", Theme.Dim);
                // Время — то, на что смотрят чаще всего: дата обычным цветом, давность акцентом.
                yield return new Segment(date, Theme.Fg);
                yield return new Segment($" ({RelativeTime.Ago(line.Date, now)})", Theme.Accent);
                break;
            case GraphLogLineKind.Body:
                yield return new Segment("  " + line.Text, Theme.Muted);
                break;
        }
    }

    /// <summary>« (HEAD → main, origin/main, tag: v1.0)» в цветах git: HEAD голубой, ветки зелёные, удалённые красные, теги жёлтые.</summary>
    private static IEnumerable<Segment> RefSegments(IReadOnlyList<CommitRef> refs)
    {
        if (refs.Count == 0)
            yield break;

        yield return new Segment(" (", Theme.Dim);
        for (var i = 0; i < refs.Count; i++)
        {
            if (i > 0)
                yield return new Segment(", ", Theme.Dim);

            var reference = refs[i];
            switch (reference.Kind)
            {
                case RefKind.Head:
                    yield return new Segment("HEAD", Theme.Info, TextStyle.Bold);
                    break;
                case RefKind.Branch when reference.IsCurrent:
                    yield return new Segment("HEAD", Theme.Info, TextStyle.Bold);
                    yield return new Segment(" → ", Theme.Dim);
                    yield return new Segment(reference.Name, Theme.Success, TextStyle.Bold);
                    break;
                case RefKind.Branch:
                    yield return new Segment(reference.Name, Theme.Success, TextStyle.Bold);
                    break;
                case RefKind.Remote:
                    yield return new Segment(reference.Name, Theme.Error);
                    break;
                default:
                    yield return new Segment("tag: ", Theme.Warning);
                    yield return new Segment(reference.Name, Theme.Warning, TextStyle.Bold);
                    break;
            }
        }

        yield return new Segment(")", Theme.Dim);
    }
}
