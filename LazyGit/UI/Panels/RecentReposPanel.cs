using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;

namespace LazyGit.UI.Panels;

/// <summary>Недавние проекты на стартовом экране: имя папки и путь (домашняя папка — «~»).</summary>
public sealed class RecentReposPanel : ListPanel<string>
{
    protected override string EmptyText => "";

    /// <summary>↑ на первой строке: фокус возвращается в поле пути.</summary>
    public event EventHandler? LeftTop;

    protected override bool OnKeyDown(Key key)
    {
        if ((key == Key.CursorUp || key == Key.K) && SelectedIndex <= 0)
        {
            LeftTop?.Invoke(this, EventArgs.Empty);
            return true;
        }

        return base.OnKeyDown(key);
    }

    protected override string KeyOf(string item) => item;

    protected override void DrawItem(int line, string path, bool selected, Color? background)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        this.DrawSegments(1, line,
        [
            Marker(selected && HasFocus),
            new Segment(name, selected && HasFocus ? Theme.Accent : Theme.Fg, TextStyle.Bold),
            new Segment("   " + TextDrawing.ShortenHome(path), Theme.Dim),
        ], Viewport.Width - 2, background);
    }
}
