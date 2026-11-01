using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>
/// Лог текущей ветки, новые коммиты сверху: граф, короткий хеш, ветки и теги, заголовок и давность справа.
/// Если вершина лога сменилась (коммит, merge, checkout), выбирается новый верхний коммит.
/// </summary>
public sealed class CommitsPanel : ListPanel<Commit>
{
    private string? _top;
    private IReadOnlyList<Commit>? _graphFor;
    private Dictionary<string, IReadOnlyList<GraphCell>> _graph = [];

    protected override string EmptyText => T("Коммитов пока нет", "No commits yet");

    /// <summary>Колонка автора перед давностью — для широкого окна графа.</summary>
    public bool ShowAuthor { get; init; }

    protected override bool KeepSelection(IReadOnlyList<Commit> items)
    {
        var top = items.Count > 0 ? items[0].Hash : null;
        var unchanged = top == _top;
        _top = top;
        return unchanged;
    }

    protected override string KeyOf(Commit item) => item.Hash;

    protected override void DrawItem(int line, Commit commit, bool selected, Color? background)
    {
        // Давность прижата к правому краю; на узкой панели её место отдаётся заголовку.
        var age = RelativeTime.Format(commit.Date, DateTimeOffset.Now);
        if (ShowAuthor && Viewport.Width >= 80)
        {
            var author = commit.AuthorName.Length > 16 ? commit.AuthorName[..15] + Theme.SymbolEllipsis : commit.AuthorName;
            age = author.PadRight(16) + "  " + age.PadLeft(6);
        }
        var showAge = Viewport.Width >= 30;
        var textWidth = Viewport.Width - 1 - (showAge ? age.Length + 2 : 1);

        var segments = new List<Segment> { Marker(selected) };
        segments.AddRange(GraphSegments(commit, textWidth / 3));
        segments.Add(new Segment(commit.ShortHash, Theme.Muted));
        foreach (var reference in commit.Refs.OrderBy(RefOrder))
            segments.Add(RefSegment(reference));
        segments.Add(new Segment(" " + commit.Subject, Theme.Fg));
        this.DrawSegments(1, line, segments, textWidth, background);

        if (showAge)
            this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }

    /// <summary>Граф строки. Шире <paramref name="maxWidth"/> обрезается с «…», чтобы заголовку осталось место.</summary>
    private IEnumerable<Segment> GraphSegments(Commit commit, int maxWidth)
    {
        if (!ReferenceEquals(_graphFor, Items))
        {
            _graphFor = Items;
            var rows = CommitGraph.Build(Items);
            _graph = new Dictionary<string, IReadOnlyList<GraphCell>>();
            for (var i = 0; i < Items.Count; i++)
                _graph[Items[i].Hash] = rows[i];
        }

        if (!_graph.TryGetValue(commit.Hash, out var cells))
            yield break;

        var cut = cells.Count > maxWidth;
        var shown = cut ? cells.Take(Math.Max(0, maxWidth - 2)) : cells;
        foreach (var cell in shown)
        {
            var color = cell.Color < 0 ? Theme.Dim : BranchColor(cell.Color);
            var style = cell.Symbol is CommitGraph.Commit or CommitGraph.Merge ? TextStyle.Bold : TextStyle.None;
            yield return new Segment(cell.Symbol.ToString(), color, style);
        }

        if (cut)
            yield return new Segment(Theme.SymbolEllipsis + " ", Theme.Dim);
    }

    /// <summary>Цвета веток по кругу: ветки, появившиеся рядом, различаются.</summary>
    public static Color BranchColor(int color) => (color % 5) switch
    {
        0 => Theme.Accent,
        1 => Theme.Cyan,
        2 => Theme.Green,
        3 => Theme.Warning,
        _ => Theme.SyntaxControlKeyword,
    };

    /// <summary>Порядок меток: текущая ветка или HEAD, локальные ветки, удалённые, теги.</summary>
    private static int RefOrder(CommitRef reference) => reference switch
    {
        { IsCurrent: true } or { Kind: RefKind.Head } => 0,
        { Kind: RefKind.Branch } => 1,
        { Kind: RefKind.Remote } => 2,
        _ => 3,
    };

    private static Segment RefSegment(CommitRef reference) => reference.Kind switch
    {
        RefKind.Head => new Segment(" HEAD", Theme.Info, TextStyle.Bold),
        RefKind.Branch when reference.IsCurrent => new Segment($" {Theme.SymbolBranch} {reference.Name}", Theme.Info, TextStyle.Bold),
        RefKind.Branch => new Segment($" {Theme.SymbolBranch} {reference.Name}", Theme.Green),
        RefKind.Remote => new Segment(" " + reference.Name, Theme.Muted),
        _ => new Segment($" {Theme.SymbolTag}{reference.Name}", Theme.Warning),
    };
}
