using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>Строки выбора репозитория: «владелец/репо  описание» и справа папка копии или «GitHub».</summary>
public sealed class RepoChoicesPanel : ListPanel<RepoChoice>
{
    public string Empty { get; set; } = "";

    protected override string EmptyText => Empty;

    protected override string KeyOf(RepoChoice item) => item.Repo.NameWithOwner.ToLowerInvariant();

    protected override void DrawItem(int line, RepoChoice choice, bool selected, Color? background)
    {
        var where = choice.Manual ? T("ввести вручную", "typed in")
            : choice.LocalPath is { } path ? TextDrawing.ShortenHome(path)
            : choice.IsPrivate ? T("GitHub · приватный", "GitHub · private") : "GitHub";
        if (where.Length > 30)
            where = Theme.SymbolEllipsis + where[^29..];
        var textWidth = Viewport.Width - where.Length - 4;

        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(choice.Repo.NameWithOwner, selected ? Theme.Accent : Theme.Fg, TextStyle.Bold),
            new Segment("  " + choice.Description, Theme.Muted),
        ], textWidth, background);
        this.DrawSegments(Viewport.Width - where.Length - 1, line,
            [new Segment(where, choice.LocalPath is null ? Theme.Dim : Theme.Success)], where.Length, background);
    }
}
