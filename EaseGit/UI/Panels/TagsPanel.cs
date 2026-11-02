using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace EaseGit.UI.Panels;

/// <summary>
/// Теги: «#v1.0  a1b2c3d  подпись   2 нед», у аннотированного перед подписью «✎». Тег с релизом на GitHub —
/// с меткой «● релиз» (последний — «● latest», черновик и пре-релиз — своими словами).
/// </summary>
public sealed class TagsPanel : ListPanel<Tag>
{
    private IReadOnlyDictionary<string, GitHubRelease> _releases = new Dictionary<string, GitHubRelease>();

    /// <summary>Релизы на GitHub по имени тега.</summary>
    public IReadOnlyDictionary<string, GitHubRelease> Releases
    {
        get => _releases;
        set
        {
            _releases = value;
            SetNeedsDraw();
        }
    }

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
            .. ReleaseBadge(tag),
            new Segment($"  {tag.ShortHash}  ", Theme.Muted),
            new Segment(tag.Annotated ? "✎ " : "", Theme.Info),
            new Segment(tag.Subject, Theme.Fg),
        ], textWidth, background);
        this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }

    private IEnumerable<Segment> ReleaseBadge(Tag tag)
    {
        if (!_releases.TryGetValue(tag.Name, out var release))
            yield break;

        var (text, color) = release switch
        {
            { IsDraft: true } => (T("черновик релиза", "draft release"), Theme.Muted),
            { IsPrerelease: true } => (T("пре-релиз", "pre-release"), Theme.Info),
            { IsLatest: true } => ("latest", Theme.Success),
            _ => (T("релиз", "release"), Theme.Success),
        };
        yield return new Segment($"  {Theme.SymbolCurrent} {text}", color);
    }
}
