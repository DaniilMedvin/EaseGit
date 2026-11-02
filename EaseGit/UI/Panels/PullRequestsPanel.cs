using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Список PR: «#12 ✓ ● заголовок   автор  2 дн». Первый значок — CI (✓ ✗ ●), второй — ревью
/// (✓ одобрен, ✗ просят правки, ● ждёт). Черновик приглушён. С <see cref="ShowRepo"/> перед номером имя репозитория.
/// </summary>
public sealed class PullRequestsPanel : ListPanel<PullRequest>
{
    /// <summary>Подпись пустого списка: зависит от вкладки.</summary>
    public string Empty { get; set; } = "";

    /// <summary>PR из разных репозиториев: показывать «EaseGit#12», а не «#12».</summary>
    public bool ShowRepo { get; set; }

    protected override string EmptyText => Empty;

    protected override string KeyOf(PullRequest item) => item.Url;

    protected override void DrawItem(int line, PullRequest pr, bool selected, Color? background)
    {
        var age = RelativeTime.Format(pr.UpdatedAt, DateTimeOffset.Now);
        var author = pr.Author.Length > 14 ? pr.Author[..13] + Theme.SymbolEllipsis : pr.Author;
        var right = $"{author,-14}  {age,6}";
        var showRight = Viewport.Width >= 50;
        var textWidth = Viewport.Width - 1 - (showRight ? right.Length + 2 : 1);

        var number = ShowRepo ? $"{pr.Repo.Name}#{pr.Number}" : $"#{pr.Number}";
        var segments = new List<Segment>
        {
            Marker(selected),
            new(number + " ", Theme.Accent, TextStyle.Bold),
            new(CheckSymbol(pr.Checks) + " ", CheckColor(pr.Checks)),
            ReviewSegment(pr.Review),
        };
        if (pr.IsDraft)
            segments.Add(new Segment(T("черновик ", "draft "), Theme.Dim, TextStyle.Italic));
        segments.Add(new Segment(pr.Title, pr.IsDraft ? Theme.Muted : Theme.Fg));
        this.DrawSegments(1, line, segments, textWidth, background);

        if (showRight)
            this.DrawSegments(Viewport.Width - right.Length - 1, line, [new Segment(right, Theme.Dim)], right.Length, background);
    }

    private static string CheckSymbol(CheckState state) => state == CheckState.None ? " " : Panels.DiffPanel.CheckSymbol(state);

    public static Color CheckColor(CheckState state) => state switch
    {
        CheckState.Success => Theme.Success,
        CheckState.Failure => Theme.Error,
        CheckState.Pending => Theme.Warning,
        _ => Theme.Dim,
    };

    private static Segment ReviewSegment(ReviewState review) => review switch
    {
        ReviewState.Approved => new Segment(Theme.SymbolSuccess + " ", Theme.Success),
        ReviewState.ChangesRequested => new Segment(Theme.SymbolError + " ", Theme.Error),
        ReviewState.Required => new Segment(Theme.SymbolCurrent + " ", Theme.Warning),
        _ => new Segment("  ", Theme.Dim),
    };
}
