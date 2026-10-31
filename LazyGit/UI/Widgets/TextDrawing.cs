using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>Цветной фрагмент строки. <paramref name="Background"/> переопределяет фон строки.</summary>
public readonly record struct Segment(string Text, Color Color, TextStyle Style = TextStyle.None, Color? Background = null)
{
    /// <summary>Плашка: текст цвета фона терминала на цветном фоне.</summary>
    public static Segment Badge(string text, Color color) => new($" {text} ", Theme.Backdrop, TextStyle.Bold, color);

    /// <summary>Мягкая плашка: цветной текст на приглушённом фоне того же цвета.</summary>
    public static Segment Pill(string text, Color color, TextStyle style = TextStyle.None) =>
        new($" {text} ", color, style, Theme.Tint(color));
}

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

            view.SetAttribute(new Attribute(segment.Color, segment.Background ?? bg, segment.Style));
            view.AddStr(x + used, y, text);
            used += text.Length;
        }

        return used;
    }

    /// <summary>
    /// Ячейка из двух пикселей по вертикали: «▀» цветом верхнего на фоне нижнего.
    /// null — прозрачный пиксель (фон терминала).
    /// </summary>
    public static void DrawHalfBlock(this View view, int x, int y, Color? top, Color? bottom)
    {
        switch (top, bottom)
        {
            case (null, null):
                return;
            case ({ } t, null):
                view.SetAttribute(new Attribute(t, Color.None));
                view.AddRune(x, y, new System.Text.Rune('▀'));
                break;
            case (null, { } b):
                view.SetAttribute(new Attribute(b, Color.None));
                view.AddRune(x, y, new System.Text.Rune('▄'));
                break;
            case ({ } t, { } b):
                view.SetAttribute(new Attribute(t, b));
                view.AddRune(x, y, new System.Text.Rune('▀'));
                break;
        }
    }

    /// <summary>Число со словом в нужной форме: «1 файл», «3 файла», «11 файлов».</summary>
    public static string Plural(int count, string one, string few, string many)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = mod10 == 1 && mod100 != 11 ? one
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few
            : many;
        return $"{count} {word}";
    }

    /// <summary>Сокращает домашний каталог до «~».</summary>
    public static string ShortenHome(string path)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return path == home || path.StartsWith(home + "/") ? "~" + path[home.Length..] : path;
    }
}
