using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Лог текущей ветки, новые коммиты сверху: короткий хеш, заголовок и давность справа.</summary>
public sealed class CommitsPanel : ListPanel<Commit>
{
    protected override string EmptyText => "Коммитов пока нет";

    protected override string KeyOf(Commit item) => item.Hash;

    protected override void DrawItem(int line, Commit commit, bool selected, Color? background)
    {
        // Давность прижата к правому краю; на узкой панели её место отдаётся заголовку.
        var age = RelativeTime.Format(commit.Date, DateTimeOffset.Now);
        var showAge = Viewport.Width >= 30;
        var textWidth = Viewport.Width - 1 - (showAge ? age.Length + 2 : 1);

        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(commit.ShortHash, Theme.Muted),
            new Segment(" " + commit.Subject, Theme.Fg),
        ], textWidth, background);

        if (showAge)
            this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }
}
