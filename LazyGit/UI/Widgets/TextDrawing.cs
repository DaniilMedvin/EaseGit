using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>Цветной фрагмент строки.</summary>
public readonly record struct Segment(string Text, Color Color, TextStyle Style = TextStyle.None);

public static class TextDrawing
{
    /// <summary>
    /// Рисует фрагменты подряд, начиная с (x, y), и обрезает строку по <paramref name="maxWidth"/> с «…».
    /// Возвращает ширину нарисованного текста.
    /// </summary>
    public static int DrawSegments(this View view, int x, int y, IEnumerable<Segment> segments, int maxWidth, Color? background = null)
    {
        var bg = background ?? Color.None;
        var used = 0;
        foreach (var segment in segments)
        {
            if (used >= maxWidth)
                break;

            var text = segment.Text;
            if (used + text.Length > maxWidth)
                text = text[..Math.Max(0, maxWidth - used - 1)] + Theme.SymbolEllipsis;

            view.SetAttribute(new Attribute(segment.Color, bg, segment.Style));
            view.AddStr(x + used, y, text);
            used += text.Length;
        }

        return used;
    }

    /// <summary>Сокращает домашний каталог до «~».</summary>
    public static string ShortenHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path == home || path.StartsWith(home + "/") ? "~" + path[home.Length..] : path;
    }
}
