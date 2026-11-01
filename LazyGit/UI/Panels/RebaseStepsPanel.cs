using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>План interactive rebase: действие, короткий хеш и заголовок, новые коммиты сверху, как в логе.</summary>
public sealed class RebaseStepsPanel : ListPanel<RebaseStep>
{
    private const int ActionWidth = 7;

    protected override string EmptyText => T("Нет коммитов", "No commits");

    protected override string KeyOf(RebaseStep item) => item.Commit.Hash;

    /// <summary>Индекс выбранного шага или -1.</summary>
    public int SelectedIndex => Selected is { } step ? Items.ToList().IndexOf(step) : -1;

    protected override void DrawItem(int line, RebaseStep step, bool selected, Color? background)
    {
        var dropped = step.Action == RebaseAction.Drop;
        var into = step.Action is RebaseAction.Squash or RebaseAction.Fixup ? $"{Theme.SymbolBehind} " : "";
        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(step.Action.ToString().ToLowerInvariant().PadRight(ActionWidth), ActionColor(step.Action), TextStyle.Bold),
            new Segment(step.Commit.ShortHash + " ", Theme.Muted),
            new Segment(into, ActionColor(step.Action)),
            new Segment(step.Subject, dropped ? Theme.Dim : Theme.Fg, dropped ? TextStyle.Strikethrough : TextStyle.None),
        ], Viewport.Width - 2, background);
    }

    public static Color ActionColor(RebaseAction action) => action switch
    {
        RebaseAction.Pick => Theme.Muted,
        RebaseAction.Reword => Theme.Info,
        RebaseAction.Edit => Theme.Warning,
        RebaseAction.Squash or RebaseAction.Fixup => Theme.Accent,
        _ => Theme.Error,
    };
}
