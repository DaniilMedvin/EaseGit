using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Файлы сравнения: плашка статуса, путь (у переименования — «старое → новое») и справа «+12 −3» или «Bin».</summary>
public sealed class ComparedFilesPanel : ListPanel<ComparedFile>
{
    protected override string EmptyText => T("Разницы нет", "No differences");

    protected override string KeyOf(ComparedFile item) => item.Path;

    protected override void DrawItem(int line, ComparedFile file, bool selected, Color? background)
    {
        var counts = file.Added is null ? "Bin" : $"+{file.Added} -{file.Deleted}";
        var textWidth = Viewport.Width - counts.Length - 4;
        var segments = new List<Segment>
        {
            Marker(selected),
            Segment.Badge(file.Status.ToString(), file.Status switch
            {
                'A' => Theme.Success,
                'D' => Theme.Error,
                'R' or 'C' => Theme.Info,
                _ => Theme.Warning,
            }),
            new(" ", Theme.Fg),
        };
        if (file.OldPath is { } old)
            segments.Add(new Segment($"{old} → ", Theme.Muted));
        segments.Add(new Segment(file.Path, selected ? Theme.Accent : Theme.Fg));
        this.DrawSegments(1, line, segments, textWidth, background);

        var x = Viewport.Width - counts.Length - 1;
        if (file.Added is null)
            this.DrawSegments(x, line, [new Segment(counts, Theme.Dim)], counts.Length, background);
        else
            this.DrawSegments(x, line, [new Segment($"+{file.Added}", Theme.Success), new Segment($" -{file.Deleted}", Theme.Error)], counts.Length, background);
    }
}
