using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace EaseGit.UI.Panels;

/// <summary>Просмотр готовых цветных строк (jobs запуска, лог): прокрутка колесом и <see cref="ScrollBy"/>.</summary>
public sealed class SegmentsPanel : View
{
    private IReadOnlyList<IReadOnlyList<Segment>> _lines = [];
    private string? _message;
    private int _scroll;

    public SegmentsPanel()
    {
        CanFocus = false;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    /// <param name="keepScroll">Те же данные после обновления — прокрутка остаётся.</param>
    public void SetLines(IReadOnlyList<IReadOnlyList<Segment>> lines, bool keepScroll = false)
    {
        _lines = lines;
        _message = null;
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

    private int MaxScroll => Math.Max(0, _lines.Count - Viewport.Height);

    public void ScrollBy(int delta)
    {
        _scroll = Math.Clamp(_scroll + delta, 0, MaxScroll);
        SetNeedsDraw();
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
        if (_message is not null)
        {
            this.DrawSegments(1, 0, [new Segment(_message, Theme.Muted)], Viewport.Width - 1);
            return true;
        }

        for (var row = 0; row < Viewport.Height && _scroll + row < _lines.Count; row++)
            this.DrawSegments(1, row, _lines[_scroll + row], Viewport.Width - 1);
        return true;
    }
}
