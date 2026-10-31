using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Анимированные волны в нижней части экрана. Цвет переливается между синим и зелёным,
/// над гребнями изредка появляются блики. При низком окне не рисуются.
/// </summary>
public sealed class Waves : AnimatedView
{
    private const string Blocks = " ▁▂▃▄▅▆▇█";
    private const int MinScreenHeight = 26;

    private double _time;

    public Waves()
    {
        Width = Dim.Fill();
        Height = 5;
    }

    protected override void OnTick() => _time = Seconds * 0.875;
    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (SuperView is null || SuperView.Viewport.Height < MinScreenHeight)
            return true;

        var width = Viewport.Width;
        var height = Viewport.Height;
        for (var x = 0; x < width; x++)
        {
            var level = WaveLevel(x, height);
            var hue = (Math.Sin(x * 0.025 + _time * 0.6) + 1) / 2;
            var baseColor = Theme.Blend(Theme.Accent, Theme.Green, hue);

            for (var row = 0; row < height; row++)
            {
                var fromBottom = height - 1 - row;
                var fill = level - fromBottom;
                if (fill <= 0)
                {
                    DrawSparkle(x, row, fromBottom, level);
                    continue;
                }

                var glyph = fill >= 1 ? '█' : Blocks[(int)(fill * (Blocks.Length - 1))];
                // Гребень ярче, глубина темнее.
                var depth = 0.45 + 0.4 * (1 - fromBottom / (double)height);
                SetAttribute(new Attribute(Theme.Blend(baseColor, Theme.Backdrop, depth), Color.None));
                AddRune(x, row, new System.Text.Rune(glyph));
            }
        }

        return true;
    }

    /// <summary>Высота волны в ячейках (дробная) для столбца x.</summary>
    private double WaveLevel(int x, int height)
    {
        var wave = 0.55 * Math.Sin(x * 0.09 + _time)
                   + 0.30 * Math.Sin(x * 0.037 - _time * 0.7)
                   + 0.15 * Math.Sin(x * 0.21 + _time * 1.6);
        return (wave * 0.5 + 0.5) * (height - 1.5) + 0.8;
    }

    private void DrawSparkle(int x, int row, int fromBottom, double level)
    {
        if (fromBottom - level > 1.5)
            return;

        var tick = (int)(_time * 3);
        var hash = (x * 73856093) ^ (tick * 19349663);
        if ((hash & 0x7FFFFFFF) % 61 != 0)
            return;

        SetAttribute(new Attribute(Theme.Dim, Color.None));
        AddRune(x, row, new System.Text.Rune('·'));
    }
}
