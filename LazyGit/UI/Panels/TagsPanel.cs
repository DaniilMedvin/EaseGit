using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Теги: «#v1.0  a1b2c3d  подпись   2 нед», у аннотированного перед подписью «✎».</summary>
public sealed class TagsPanel : ListPanel<Tag>
{
    protected override string EmptyText => T("Тегов нет. n — поставить на HEAD", "No tags. n tags HEAD");

    protected override string KeyOf(Tag item) => item.Name;

    protected override void DrawItem(int line, Tag tag, bool selected, Color? background)
    {
        var age = RelativeTime.Format(tag.Date, DateTimeOffset.Now);
        var textWidth = Viewport.Width - age.Length - 4;
        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment($"{Theme.SymbolTag}{tag.Name}", Theme.Warning, TextStyle.Bold),
            new Segment($"  {tag.ShortHash}  ", Theme.Muted),
            new Segment(tag.Annotated ? "✎ " : "", Theme.Info),
            new Segment(tag.Subject, Theme.Fg),
        ], textWidth, background);
        this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }
}
