using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Worktrees: «● ⎇ main   ✓ чисто   основной   ~/proj», «  ⎇ feature/x   3 изменения   ~/proj-feature-x», detached —
/// короткий хеш; пометки «основной», «заблокирован», «папки нет»; путь — последним.
/// </summary>
public sealed class WorktreesPanel : ListPanel<Worktree>
{
    /// <summary>Папка открытого сейчас worktree: отмечается «●».</summary>
    public string Current { get; set; } = "";

    /// <summary>Сколько изменений в каждой папке (null — неизвестно).</summary>
    public IReadOnlyDictionary<string, int?> Changes { get; set; } = new Dictionary<string, int?>();

    protected override string EmptyText => "";

    protected override string KeyOf(Worktree item) => item.Path;

    protected override void DrawItem(int line, Worktree worktree, bool selected, Color? background)
    {
        var current = worktree.Path == Current;
        var head = worktree.Branch is { } branch ? $"{Theme.SymbolBranch} {branch}" : $"{worktree.ShortHead} (detached)";
        var segments = new List<Segment>
        {
            Marker(selected),
            new(current ? Theme.SymbolCurrent + " " : "  ", Theme.Info),
            new(head, current ? Theme.Info : worktree.Branch is null ? Theme.Warning : Theme.Fg, TextStyle.Bold),
        };

        if (worktree.Prunable is not null)
            segments.Add(new Segment(T("   папки нет (p — убрать запись)", "   folder is gone (p removes the entry)"), Theme.Error));
        else if (Changes.GetValueOrDefault(worktree.Path) is { } changes)
            segments.Add(changes == 0
                ? new Segment($"   {Theme.SymbolSuccess} {T("чисто", "clean")}", Theme.Success)
                : new Segment($"   {Plural(changes, "изменение", "изменения", "изменений", "change", "changes")}", Theme.Warning));
        if (worktree.IsMain)
            segments.Add(new Segment(T("   основной", "   main"), Theme.Dim));
        if (worktree.Locked is { } reason)
            segments.Add(new Segment(T($"   заблокирован{(reason.Length > 0 ? $": {reason}" : "")}", $"   locked{(reason.Length > 0 ? $": {reason}" : "")}"), Theme.Warning));
        // Путь — последним: длинный обрежется, а состояние останется видно.
        segments.Add(new Segment("   " + TextDrawing.ShortenHome(worktree.Path), Theme.Dim));
        this.DrawSegments(1, line, segments, Viewport.Width - 2, background);
    }
}
