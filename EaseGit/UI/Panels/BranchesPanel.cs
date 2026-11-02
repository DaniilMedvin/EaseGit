using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Локальные ветки. Текущая отмечена «●» и выделена цветом info, справа — ↑ahead ↓behind относительно upstream.
/// После обновления по умолчанию выбрана текущая ветка; после checkout выбор переходит на новую текущую.
/// </summary>
public sealed class BranchesPanel : ListPanel<Branch>
{
    private string? _current;

    protected override string EmptyText => T("Веток пока нет", "No branches yet");

    protected override bool KeepSelection(IReadOnlyList<Branch> items)
    {
        var current = items.FirstOrDefault(b => b.IsCurrent)?.Name;
        var unchanged = current == _current;
        _current = current;
        return unchanged;
    }

    protected override string KeyOf(Branch item) => item.Name;

    protected override int DefaultIndex(IReadOnlyList<Branch> items)
    {
        for (var i = 0; i < items.Count; i++)
        {
            if (items[i].IsCurrent)
                return i;
        }

        return base.DefaultIndex(items);
    }

    /// <summary>Ветки, открытые в других worktree: ветка → папка. Переключиться на них здесь git не даст.</summary>
    public IReadOnlyDictionary<string, string> InWorktrees { get; set; } = new Dictionary<string, string>();

    /// <summary>Ветки, влитые копиями (rebase / squash): плашка «≈ влита» после имени.</summary>
    public IReadOnlyDictionary<string, MergedCopy> Merged { get; set; } = new Dictionary<string, MergedCopy>();

    protected override void DrawItem(int line, Branch branch, bool selected, Color? background)
    {
        var track = Track(branch).ToList();
        var trackWidth = track.Sum(s => s.Text.Length);
        var nameWidth = Viewport.Width - 1 - (trackWidth > 0 ? trackWidth + 2 : 1);

        this.DrawSegments(1, line,
        [
            Marker(selected),
            branch.IsCurrent
                ? new Segment(Theme.SymbolCurrent + " ", Theme.Info)
                : new Segment("  ", Theme.Fg),
            branch.IsCurrent
                ? new Segment(branch.Name, Theme.Info, TextStyle.Bold)
                : new Segment(branch.Name, Theme.Fg),
            Merged.ContainsKey(branch.Name) ? new Segment(T("  ≈ влита", "  ≈ merged"), Theme.Info, TextStyle.Italic) : new Segment("", Theme.Fg),
            InWorktrees.TryGetValue(branch.Name, out var folder) ? new Segment($"  ⌂ {Path.GetFileName(folder)}", Theme.Dim) : new Segment("", Theme.Fg),
        ], nameWidth, background);

        if (trackWidth > 0)
            this.DrawSegments(Viewport.Width - trackWidth - 1, line, track, trackWidth, background);
    }

    /// <summary>Состояние относительно upstream: «↑2 ↓1», «upstream удалён» или ничего.</summary>
    private static IEnumerable<Segment> Track(Branch branch)
    {
        if (branch.IsUpstreamGone)
        {
            yield return new Segment(T("upstream удалён", "upstream gone"), Theme.Dim);
            yield break;
        }

        if (branch.Ahead > 0)
            yield return new Segment($"{Theme.SymbolAhead}{branch.Ahead}", Theme.Success);
        if (branch.Ahead > 0 && branch.Behind > 0)
            yield return new Segment(" ", Theme.Fg);
        if (branch.Behind > 0)
            yield return new Segment($"{Theme.SymbolBehind}{branch.Behind}", Theme.Warning);
    }
}
