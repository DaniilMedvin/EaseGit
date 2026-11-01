using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Список stash, последние сверху: номер, сообщение и давность справа.</summary>
public sealed class StashPanel : ListPanel<StashEntry>
{
    protected override string EmptyText => T("Stash пуст", "Stash is empty");

    protected override string KeyOf(StashEntry item) => item.Hash;

    protected override void DrawItem(int line, StashEntry stash, bool selected, Color? background)
    {
        var age = RelativeTime.Format(stash.Date, DateTimeOffset.Now);
        var showAge = Viewport.Width >= 30;
        var textWidth = Viewport.Width - 1 - (showAge ? age.Length + 2 : 1);

        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment($"{stash.Index} ", Theme.Muted),
            new Segment(stash.Message, Theme.Fg),
        ], textWidth, background);

        if (showAge)
            this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }
}
