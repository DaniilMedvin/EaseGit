using LazyGit.DotNet;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Panels;

/// <summary>
/// Вывод dotnet: ошибки error, предупреждения warning, упавшие тесты error жирным, итоги accent. Пока внизу — новые строки
/// прокручивают сами; прокрутил вверх (↑ PageUp, колесо) — остаётся на месте, End — снова следить за концом.
/// С <see cref="OnlyProblems"/> видны только ошибки, предупреждения, упавшие тесты и итоги.
/// </summary>
public sealed class OutputPanel : View
{
    private readonly List<(string Text, DotnetLineKind Kind)> _lines = [];
    private int _scroll;
    private bool _follow = true;
    private bool _onlyProblems;

    public OutputPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
    }

    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            _onlyProblems = value;
            _follow = true;
            ScrollToEnd();
        }
    }

    public void Clear()
    {
        _lines.Clear();
        _scroll = 0;
        _follow = true;
        SetNeedsDraw();
    }

    public void Append(IEnumerable<string> lines)
    {
        _lines.AddRange(lines.Select(l => (l, DotnetOutput.Classify(l))));
        if (_follow)
            ScrollToEnd();
        else
            SetNeedsDraw();
    }

    private List<(string Text, DotnetLineKind Kind)> Shown =>
        _onlyProblems ? _lines.Where(l => l.Kind != DotnetLineKind.Normal).ToList() : _lines;

    private void ScrollToEnd()
    {
        _scroll = Math.Max(0, Shown.Count - Viewport.Height);
        SetNeedsDraw();
    }

    public void ScrollBy(int delta)
    {
        var max = Math.Max(0, Shown.Count - Viewport.Height);
        _scroll = Math.Clamp(_scroll + delta, 0, max);
        _follow = _scroll >= max;
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        var page = Math.Max(1, Viewport.Height - 1);
        if (key == Key.CursorDown || key == Key.J)
            ScrollBy(1);
        else if (key == Key.CursorUp || key == Key.K)
            ScrollBy(-1);
        else if (key == Key.PageDown)
            ScrollBy(page);
        else if (key == Key.PageUp)
            ScrollBy(-page);
        else if (key == Key.Home)
            ScrollBy(-Shown.Count);
        else if (key == Key.End)
            ScrollBy(Shown.Count);
        else
            return base.OnKeyDown(key);
        return true;
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

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var shown = Shown;
        for (var row = 0; row < Viewport.Height && _scroll + row < shown.Count; row++)
        {
            var (text, kind) = shown[_scroll + row];
            var (color, style) = kind switch
            {
                DotnetLineKind.Error => (Theme.Error, TextStyle.None),
                DotnetLineKind.Warning => (Theme.Warning, TextStyle.None),
                DotnetLineKind.FailedTest => (Theme.Error, TextStyle.Bold),
                DotnetLineKind.Summary => (Theme.Accent, TextStyle.Bold),
                _ => (Theme.Muted, TextStyle.None),
            };
            this.DrawSegments(1, row, [new Segment(text.Replace("\t", "    "), color, style)], Viewport.Width - 1);
        }

        return true;
    }
}
