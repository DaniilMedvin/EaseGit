using Terminal.Gui.ViewBase;

namespace EaseGit.UI.Widgets;

/// <param name="Optional">Второстепенная подсказка: убирается первой, если строка не помещается.</param>
public sealed record Hint(string Key, string Description, bool Optional = false);

/// <summary>
/// Строка подсказок по клавишам: «клавиша описание · клавиша описание».
/// Если не помещается, сначала убираются второстепенные подсказки (с конца), затем обрезается с «…».
/// </summary>
public sealed class HintBar : View
{
    private IReadOnlyList<Hint> _hints = [];

    public HintBar()
    {
        Width = Dim.Fill();
        Height = 1;
        CanFocus = false;
    }

    public void SetHints(IEnumerable<Hint> hints)
    {
        _hints = hints.ToList();
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var shown = _hints.ToList();
        while (TotalWidth(shown) > Viewport.Width && shown.FindLastIndex(h => h.Optional) is var index and >= 0)
            shown.RemoveAt(index);

        this.DrawSegments(0, 0, Segments(shown), Viewport.Width);
        return true;
    }

    private static int TotalWidth(IReadOnlyList<Hint> hints) => Segments(hints).Sum(s => s.Text.Length);

    private static IEnumerable<Segment> Segments(IReadOnlyList<Hint> hints)
    {
        for (var i = 0; i < hints.Count; i++)
        {
            if (i > 0)
                yield return new Segment($" {Theme.SymbolSeparator} ", Theme.Muted);
            yield return new Segment(hints[i].Key, Theme.Fg);
            yield return new Segment(" " + hints[i].Description, Theme.Muted);
        }
    }
}
