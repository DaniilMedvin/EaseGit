using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;

namespace LazyGit.UI.Panels;

/// <summary>Список путей файлов: имя файла ярко, папка перед ним приглушена.</summary>
public sealed class PathsPanel : ListPanel<string>
{
    public string Empty { get; set; } = "";

    protected override string EmptyText => Empty;

    protected override string KeyOf(string item) => item;

    protected override void DrawItem(int line, string path, bool selected, Color? background)
    {
        var slash = path.LastIndexOf('/');
        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(path[..(slash + 1)], Theme.Dim),
            new Segment(path[(slash + 1)..], selected ? Theme.Accent : Theme.Fg, TextStyle.Bold),
        ], Viewport.Width - 2, background);
    }
}
