using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Значок C# в левом верхнем углу: шестиугольник из трёх граней разной яркости с кольцом «C» и знаком «#».
/// Окрашен градиентом темы, медленно дрейфует, по граням изредка проходит блик.
/// Рисуется полублоками «▀▄»: одна ячейка — два пикселя по вертикали.
/// </summary>
public sealed class CSharpLogo
{
    private const int RegionX = 2;
    private const int RegionY = 1;
    private const int RegionWidth = 24;
    private const int RegionRows = 10;
    private const int MinScreenWidth = 70;
    private const int MinScreenHeight = 22;

    private const double CenterX = 11;
    private const double CenterY = 9.5;
    private const double Radius = 8.5;
    private const double GlowWidth = 2;
    private const double DriftX = 2.5;
    private const double DriftY = 1;

    // Кольцо «C»: центр левее центра шестиугольника, разрыв справа.
    private const double RingOffsetX = -2.3;
    private const double RingInner = 2.1;
    private const double RingOuter = 4.1;
    private const double RingGapDegrees = 40;

    // Знак «#» 5×5 пикселей, левый верхний угол относительно центра шестиугольника.
    private const double HashOffsetX = 1.3;
    private const double HashOffsetY = -2.5;
    private static readonly string[] Hash = [" # # ", "#####", " # # ", "#####", " # # "];

    private static readonly double HalfWidth = Radius * Math.Sqrt(3) / 2;
    private static readonly Color SymbolColor = new("#EEF1F8");

    public void Draw(View view, double seconds, List<System.Drawing.Rectangle> occupied)
    {
        if (view.Viewport.Width < MinScreenWidth || view.Viewport.Height < MinScreenHeight)
            return;

        var cx = CenterX + DriftX * Math.Sin(seconds * 0.12);
        var cy = CenterY + DriftY * Math.Sin(seconds * 0.09 + 1.1);
        var shine = ShinePosition(seconds);

        for (var row = 0; row < RegionRows; row++)
        {
            for (var col = 0; col < RegionWidth; col++)
            {
                var x = RegionX + col;
                var y = RegionY + row;
                if (Starfield.IsOccupied(occupied, x, y))
                    continue;

                var top = PixelColor(col + 0.5, row * 2 + 0.5, cx, cy, shine);
                var bottom = PixelColor(col + 0.5, row * 2 + 1.5, cx, cy, shine);
                DrawCell(view, x, y, top, bottom);
            }
        }
    }

    /// <summary>Положение блика вдоль диагонали; большую часть времени блик за пределами значка.</summary>
    private static double ShinePosition(double seconds)
    {
        const double period = 9;
        var phase = seconds % period / period;
        return -30 + phase * 90;
    }

    private static Color? PixelColor(double px, double py, double cx, double cy, double shine)
    {
        var dx = px - cx;
        var dy = py - cy;
        var hexDistance = HexDistance(Math.Abs(dx), Math.Abs(dy));

        if (hexDistance > 0)
        {
            if (hexDistance > GlowWidth)
                return null;

            var glow = 1 - hexDistance / GlowWidth;
            return glow < 0.2 ? null : Theme.Blend(Theme.Backdrop, Theme.Accent, 0.1 + 0.15 * glow);
        }

        var gradient = Math.Clamp((dy + Radius) / (2 * Radius), 0, 1);
        var surface = Theme.Blend(Theme.Accent, Theme.Green, gradient);

        Color color;
        if (IsSymbol(dx, dy))
        {
            color = SymbolColor;
        }
        else
        {
            // Грани как у куба: левая светлее, нижняя темнее.
            var angle = (Math.Atan2(-dy, dx) * 180 / Math.PI + 360) % 360;
            color = angle switch
            {
                >= 90 and < 210 => Theme.Blend(surface, SymbolColor, 0.12),
                >= 210 and < 330 => Theme.Blend(surface, Theme.Backdrop, 0.38),
                _ => Theme.Blend(surface, Theme.Backdrop, 0.15),
            };
        }

        var shineDistance = Math.Abs(px + py * 0.6 - (cx + shine));
        return shineDistance < 1.5 ? Theme.Blend(color, SymbolColor, 0.3 * (1 - shineDistance / 1.5)) : color;
    }

    /// <summary>Расстояние от точки до шестиугольника (с вершинами сверху и снизу); 0 и меньше — внутри.</summary>
    private static double HexDistance(double dx, double dy)
    {
        var side = dx - HalfWidth;
        var slope = dy - (Radius - dx / Math.Sqrt(3));
        return Math.Max(side, slope * Math.Sqrt(3) / 2);
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
        return hx is >= 0 and < 5 && hy is >= 0 and < 5 && Hash[hy][hx] == '#';
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
