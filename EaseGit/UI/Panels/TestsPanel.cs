using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Тесты по классам: «▸ [x] CommitGraphTests  5/5 ✓», внутри — «    [ ] Build_Linear… ✗». [x] — выбран целиком,
/// [-] — частично, [ ] — исключён. Отметки ✓ / ✗ — результат последнего прогона.
/// </summary>
public sealed class TestsPanel : ListPanel<TestRow>
{
    protected override string EmptyText => T("Тесты появятся после сборки", "Tests appear after the build");

    protected override string KeyOf(TestRow item) => item.Key;

    protected override void DrawItem(int line, TestRow row, bool selected, Color? background)
    {
        var (box, boxColor) = row.Selected == 0 ? ("[ ]", Theme.Dim) : row.Selected == row.Total ? ("[x]", Theme.Accent) : ("[-]", Theme.Info);
        var segments = new List<Segment> { Marker(selected) };
        if (row.IsClass)
            segments.Add(new Segment((row.Expanded ? Theme.SymbolExpanded : Theme.SymbolCollapsed) + " ", Theme.Dim));
        else
            segments.Add(new Segment("    ", Theme.Dim));
        segments.Add(new Segment(box + " ", boxColor, TextStyle.Bold));
        segments.Add(new Segment(row.Label, row.Selected == 0 ? Theme.Dim : Theme.Fg, row.IsClass ? TextStyle.Bold : TextStyle.None));
        if (row.IsClass)
            segments.Add(new Segment($"  {row.Selected}/{row.Total}", Theme.Dim));
        if (row.Failed)
            segments.Add(new Segment($" {Theme.SymbolError}", Theme.Error, TextStyle.Bold));
        else if (row.Passed)
            segments.Add(new Segment($" {Theme.SymbolSuccess}", Theme.Success));
        this.DrawSegments(1, line, segments, Viewport.Width - 2, background);
    }
}
