using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Полная луна в левом верхнем углу: медленно плавает, окрашена градиентом темы
/// и окружена мягким свечением. Рисуется полублоками «▀▄»: одна ячейка — два пикселя по вертикали.
/// </summary>
public sealed class Moon
{
    private const int RegionX = 3;
    private const int RegionY = 1;
    private const int RegionWidth = 22;
    private const int RegionRows = 7;
    private const int MinScreenWidth = 70;
    private const int MinScreenHeight = 18;

    private const double CenterX = 11;
    private const double CenterY = 7;
    private const double Radius = 4.4;
    private const double GlowWidth = 2.2;
    private const double DriftX = 3;
    private const double DriftY = 1.2;

    /// <summary>Кратеры: смещение от центра и радиус в пикселях.</summary>
    private static readonly (double X, double Y, double R)[] Craters =
    [
        (-1.6, -1.2, 1.0),
        (1.4, 1.3, 0.8),
        (1.9, -1.7, 0.6),
        (-0.6, 2.2, 0.5),
    ];

    public void Draw(View view, double seconds, List<System.Drawing.Rectangle> occupied)
    {
        if (view.Viewport.Width < MinScreenWidth || view.Viewport.Height < MinScreenHeight)
            return;

        // Медленный дрейф по эллипсу.
        var cx = CenterX + DriftX * Math.Sin(seconds * 0.12);
        var cy = CenterY + DriftY * Math.Sin(seconds * 0.09 + 1.1);

        for (var row = 0; row < RegionRows; row++)
        {
            for (var col = 0; col < RegionWidth; col++)
            {
                var x = RegionX + col;
                var y = RegionY + row;
                if (Starfield.IsOccupied(occupied, x, y))
                    continue;

                var top = PixelColor(col + 0.5, row * 2 + 0.5, cx, cy);
                var bottom = PixelColor(col + 0.5, row * 2 + 1.5, cx, cy);
                DrawCell(view, x, y, top, bottom);
            }
        }
    }

    private static Color? PixelColor(double px, double py, double cx, double cy)
    {
        var distance = Distance(px, py, cx, cy);
        if (distance > Radius + GlowWidth)
            return null;

        // Вертикальный градиент темы, как у логотипа.
        var gradient = Math.Clamp((py - (cy - Radius)) / (2 * Radius), 0, 1);
        var surface = Theme.Blend(Theme.Accent, Theme.Green, gradient);

        if (distance > Radius)
        {
            var glow = 1 - (distance - Radius) / GlowWidth;
            return glow < 0.15 ? null : Theme.Blend(Theme.Backdrop, surface, 0.12 + 0.18 * glow);
        }

        // Край диска чуть темнее центра.
        var limb = distance / Radius;
        var shade = 0.15 * limb * limb;
        foreach (var crater in Craters)
        {
            if (Distance(px, py, cx + crater.X, cy + crater.Y) < crater.R)
                shade += 0.18;
        }

        return Theme.Blend(surface, Theme.Backdrop, shade);
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

    private static double Distance(double x1, double y1, double x2, double y2) =>
        Math.Sqrt((x1 - x2) * (x1 - x2) + (y1 - y2) * (y1 - y2));
}
