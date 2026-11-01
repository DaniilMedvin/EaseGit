using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Issues: «#12 заголовок  bug · ui   💬3   автор  2 дн» — метки info, число комментариев, исполнитель.</summary>
public sealed class IssuesPanel : ListPanel<Issue>
{
    public string Empty { get; set; } = "";

    protected override string EmptyText => Empty;

    protected override string KeyOf(Issue item) => item.Url;

    protected override void DrawItem(int line, Issue issue, bool selected, Color? background)
    {
        var who = issue.Assignees.Count > 0 ? $"→ {issue.Assignees[0]}" : issue.Author;
        who = who.Length > 16 ? who[..15] + Theme.SymbolEllipsis : who;
        var right = $"{who,-16}  {RelativeTime.Format(issue.UpdatedAt, DateTimeOffset.Now),6}";
        var showRight = Viewport.Width >= 60;
        var textWidth = Viewport.Width - 1 - (showRight ? right.Length + 2 : 1);

        var segments = new List<Segment>
        {
            Marker(selected),
            new($"#{issue.Number} ", Theme.Accent, TextStyle.Bold),
            new(issue.Title, Theme.Fg),
        };
        if (issue.Labels.Count > 0)
            segments.Add(new Segment("  " + string.Join(" · ", issue.Labels), Theme.Info));
        if (issue.CommentCount > 0)
            segments.Add(new Segment($"  ✉{issue.CommentCount}", Theme.Muted));
        this.DrawSegments(1, line, segments, textWidth, background);
        if (showRight)
            this.DrawSegments(Viewport.Width - right.Length - 1, line, [new Segment(right, Theme.Dim)], right.Length, background);
    }
}
