using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Значок C# в левом верхнем углу: шестиугольник из трёх граней разной яркости с кольцом «C» и знаком «#».
/// Окрашен градиентом темы. Рисуется полублоками «▀▄»: одна ячейка — два пикселя по вертикали.
/// </summary>
public sealed class CSharpLogo
{
    private const int RegionX = 3;
    private const int RegionY = 1;
    private const int MinScreenWidth = 90;
    private const int MinScreenHeight = 24;

    private const double Radius = 11;
    private static readonly double HalfWidth = Radius * Math.Sqrt(3) / 2;
    private static readonly double CenterX = HalfWidth + 0.5;
    private const double CenterY = Radius + 0.5;
    private static readonly int RegionWidth = (int)Math.Ceiling(2 * HalfWidth) + 1;
    private static readonly int RegionRows = (int)Math.Ceiling(Radius) + 1;

    // Кольцо «C»: центр левее центра шестиугольника, разрыв справа.
    private const double RingOffsetX = -3;
    private const double RingInner = 2.9;
    private const double RingOuter = 5.4;
    private const double RingGapDegrees = 42;

    // Знак «#» 6×6 пикселей, левый верхний угол относительно центра шестиугольника.
    private const double HashOffsetX = 1.6;
    private const double HashOffsetY = -3;
    private static readonly string[] Hash = [" #  # ", "######", " #  # ", " #  # ", "######", " #  # "];

    private static readonly Color SymbolColor = new("#EEF1F8");

    public void Draw(View view, List<System.Drawing.Rectangle> occupied)
    {
        if (view.Viewport.Width < MinScreenWidth || view.Viewport.Height < MinScreenHeight)
            return;

        for (var row = 0; row < RegionRows; row++)
        {
            for (var col = 0; col < RegionWidth; col++)
            {
                var x = RegionX + col;
                var y = RegionY + row;
                if (Starfield.IsOccupied(occupied, x, y))
                    continue;

                var top = PixelColor(col + 0.5, row * 2 + 0.5);
                var bottom = PixelColor(col + 0.5, row * 2 + 1.5);
                DrawCell(view, x, y, top, bottom);
            }
        }
    }

    private static Color? PixelColor(double px, double py)
    {
        var dx = px - CenterX;
        var dy = py - CenterY;

        // Шестиугольник с вершинами сверху и снизу и вертикальными боковыми сторонами.
        if (Math.Abs(dx) > HalfWidth || Math.Abs(dy) > Radius - Math.Abs(dx) / Math.Sqrt(3))
            return null;

        if (IsSymbol(dx, dy))
            return SymbolColor;

        var gradient = Math.Clamp((dy + Radius) / (2 * Radius), 0, 1);
        var surface = Theme.Blend(Theme.Accent, Theme.Green, gradient);

        // Грани как у куба: левая светлее, нижняя темнее.
        var angle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
        return angle switch
        {
            >= 90 and < 210 => Theme.Blend(surface, SymbolColor, 0.12),
            >= 210 and < 330 => Theme.Blend(surface, Theme.Backdrop, 0.4),
            _ => Theme.Blend(surface, Theme.Backdrop, 0.18),
        };
    }

    private static bool IsSymbol(double dx, double dy)
    {
        var ringX = dx - RingOffsetX;
        var distance = Math.Sqrt(ringX * ringX + dy * dy);
        var angle = Math.Atan2(-dy, ringX) * 180 / Math.PI;
        if (distance >= RingInner && distance <= RingOuter && Math.Abs(angle) > RingGapDegrees)
            return true;

        var hx = (int)Math.Floor(dx - HashOffsetX);
        var hy = (int)Math.Floor(dy - HashOffsetY);
        return hy >= 0 && hy < Hash.Length && hx >= 0 && hx < Hash[hy].Length && Hash[hy][hx] == '#';
    }

    private static void DrawCell(View view, int x, int y, Color? top, Color? bottom)
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
}
