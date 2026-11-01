using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Запуски Actions: «✓ CI #6  feat: file history   ⎇ main  173ab12   push  2 ч».</summary>
public sealed class ActionsRunsPanel : ListPanel<ActionsRun>
{
    /// <summary>Коммит, запуски которого отмечены «●» слева: текущий HEAD.</summary>
    public string? HeadSha { get; set; }

    protected override string EmptyText => T("Запусков нет", "No runs");

    protected override string KeyOf(ActionsRun item) => item.Id.ToString();

    protected override void DrawItem(int line, ActionsRun run, bool selected, Color? background)
    {
        var (symbol, color) = Symbol(run.State, run.Conclusion);
        var right = $"{Theme.SymbolBranch} {Trim(run.Branch, 20)}  {run.ShortSha}  {run.Event,-12} {RelativeTime.Format(run.CreatedAt, DateTimeOffset.Now),6}";
        var showRight = Viewport.Width >= 70;
        var textWidth = Viewport.Width - 1 - (showRight ? right.Length + 2 : 1);
        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(run.Sha == HeadSha ? "● " : "  ", Theme.Info),
            new Segment(symbol + " ", color, TextStyle.Bold),
            new Segment($"{run.Workflow} #{run.Number}  ", Theme.Accent),
            new Segment(run.Title, Theme.Fg),
        ], textWidth, background);
        if (showRight)
            this.DrawSegments(Viewport.Width - right.Length - 1, line, [new Segment(right, Theme.Dim)], right.Length, background);
    }

    /// <summary>✓ / ✗ / ● и цвет; отменённый — «⊘» приглушённо, а не как провал.</summary>
    public static (string Symbol, Color Color) Symbol(CheckState state, string conclusion) => state switch
    {
        CheckState.Success => (conclusion == "skipped" ? "–" : Theme.SymbolSuccess, conclusion == "skipped" ? Theme.Dim : Theme.Success),
        CheckState.Pending => (Theme.SymbolCurrent, Theme.Warning),
        _ when conclusion == "cancelled" => ("⊘", Theme.Muted),
        _ => (Theme.SymbolError, Theme.Error),
    };

    private static string Trim(string text, int max) => text.Length > max ? text[..(max - 1)] + Theme.SymbolEllipsis : text;
}
