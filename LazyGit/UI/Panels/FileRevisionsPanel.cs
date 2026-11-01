using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>История файла: «a1b2c3d  M  заголовок   автор  2 дн», у переименования — «← старое имя».</summary>
public sealed class FileRevisionsPanel : ListPanel<FileRevision>
{
    protected override string EmptyText => T("Истории нет: файл ещё не коммитили", "No history: the file was never committed");

    protected override string KeyOf(FileRevision item) => item.Commit.Hash;

    protected override void DrawItem(int line, FileRevision revision, bool selected, Color? background)
    {
        var commit = revision.Commit;
        var author = commit.AuthorName.Length > 16 ? commit.AuthorName[..15] + Theme.SymbolEllipsis : commit.AuthorName;
        var right = $"{author,-16}  {RelativeTime.Format(commit.Date, DateTimeOffset.Now),6}";
        var showRight = Viewport.Width >= 60;
        var textWidth = Viewport.Width - 1 - (showRight ? right.Length + 2 : 1);

        var segments = new List<Segment>
        {
            Marker(selected),
            new(commit.ShortHash + " ", Theme.Muted),
            Segment.Badge(revision.Status.ToString(), StatusColor(revision.Status)),
            new(" " + commit.Subject, Theme.Fg),
        };
        if (revision.OldPath is { } old)
            segments.Add(new Segment($"  ← {old}", Theme.Info));
        this.DrawSegments(1, line, segments, textWidth, background);

        if (showRight)
            this.DrawSegments(Viewport.Width - right.Length - 1, line, [new Segment(right, Theme.Dim)], right.Length, background);
    }

    /// <summary>Цвета плашек — как у файлов: добавлен success, удалён error, переименован info, изменён warning.</summary>
    private static Color StatusColor(char status) => status switch
    {
        'A' => Theme.Success,
        'D' => Theme.Error,
        'R' or 'C' => Theme.Info,
        _ => Theme.Warning,
    };
}
