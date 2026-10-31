using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Звёздное небо на фоне: мерцающие звёзды (у краёв экрана ярче и чаще), полная луна
/// и изредка падающая звезда.
/// Звёзды не рисуются поверх других элементов окна. Располагается в координатах (0, 0) окна.
/// </summary>
public sealed class Starfield : AnimatedView
{
    // Одна звезда на N ячеек: в центре экрана и у краёв.
    private const int CenterDensity = 130;
    private const int EdgeDensity = 60;
    private static readonly char[] StarGlyphs = ['·', '·', '·', '·', '·', '∙', '∙', '⋆'];
    private static readonly char[] EdgeStarGlyphs = ['·', '·', '∙', '•', '•', '⋆', '⋆', '✧'];
    private static readonly Color StarColor = new("#E0E2EA");

    private readonly Random _random = new();
    private readonly Moon _moon = new();
    private Meteor? _meteor;
    private double _nextMeteorAt = 2.5;

    public Starfield()
    {
        X = 0;
        Y = 0;
        Width = Dim.Fill();
    }

    protected override void OnTick()
    {
        if (_meteor is not null)
        {
            _meteor.Advance();
            if (_meteor.IsFinished(Viewport.Width, Viewport.Height))
            {
                _meteor = null;
                _nextMeteorAt = Seconds + 4 + _random.NextDouble() * 7;
            }
        }
        else if (Seconds >= _nextMeteorAt && Viewport.Width > 20)
        {
            _meteor = Meteor.Spawn(_random, Viewport.Width, Viewport.Height);
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var occupied = OccupiedAreas();
        DrawStars(occupied);
        _meteor?.Draw(this, occupied);
        _moon.Draw(this, Seconds, occupied);
        return true;
    }

    private void DrawStars(List<System.Drawing.Rectangle> occupied)
    {
        var halfWidth = Viewport.Width / 2.0;
        var halfHeight = Math.Max(1, Viewport.Height / 2.0);
        for (var y = 0; y < Viewport.Height; y++)
        {
            for (var x = 0; x < Viewport.Width; x++)
            {
                var edge = EdgeFactor(x, y, halfWidth, halfHeight);
                var hash = Hash(x, y);
                var density = CenterDensity + (EdgeDensity - CenterDensity) * edge;
                if (hash % 10000 >= 10000 / density || IsOccupied(occupied, x, y))
                    continue;

                var glyphs = edge > 0.6 ? EdgeStarGlyphs : StarGlyphs;
                var glyph = glyphs[hash / 10000 % glyphs.Length];
                var phase = hash % 628 / 100.0;
                var speed = 0.6 + hash % 5 * 0.35;
                var twinkle = (Math.Sin(Seconds * speed + phase) + 1) / 2;
                var brightness = 0.12 + 0.3 * edge + (0.3 + 0.4 * edge) * twinkle;
                // Звёзды у краёв слегка окрашены в акцентный цвет.
                var tint = Theme.Blend(StarColor, Theme.Accent, 0.35 * edge);
                var color = Theme.Blend(Theme.Backdrop, tint, Math.Min(1, brightness));

                SetAttribute(new Attribute(color, Color.None, edge > 0.6 ? TextStyle.Bold : TextStyle.None));
                AddRune(x, y, new System.Text.Rune(glyph));
            }
        }
    }

    /// <summary>0 в центре экрана, 1 у краёв.</summary>
    private static double EdgeFactor(int x, int y, double halfWidth, double halfHeight)
    {
        var dx = (x - halfWidth) / halfWidth;
        var dy = (y - halfHeight) / halfHeight;
        var distance = Math.Sqrt(dx * dx + dy * dy) / Math.Sqrt(2);
        return Math.Clamp((distance - 0.25) / 0.5, 0, 1);
    }

    /// <summary>Области других элементов окна с отступом в одну ячейку.</summary>
    private List<System.Drawing.Rectangle> OccupiedAreas()
    {
        var areas = new List<System.Drawing.Rectangle>();
        if (SuperView is null)
            return areas;

        foreach (var view in SuperView.SubViews)
        {
            if (view == this || view is AnimatedView || !view.Visible)
                continue;

            var frame = view.Frame;
            frame.Inflate(1, 0);
            areas.Add(frame);
        }

        return areas;
    }

    internal static bool IsOccupied(List<System.Drawing.Rectangle> occupied, int x, int y)
    {
        foreach (var area in occupied)
        {
            if (area.Contains(x, y))
                return true;
        }

        return false;
    }

    private static int Hash(int x, int y)
    {
        unchecked
        {
            var h = x * 374761393 + y * 668265263;
            h = (h ^ (h >> 13)) * 1274126177;
            return (h ^ (h >> 16)) & 0x7FFFFFFF;
        }
    }

    /// <summary>Падающая звезда: появляется у края экрана и летит вниз, от центра наружу. Хвост постепенно гаснет.</summary>
    private sealed class Meteor
    {
        private const double StepX = 1.6;
        private const double StepY = 0.8;

        private readonly int _direction;

        private double _x;
        private double _y;
        private int _age;
        private readonly int _length;
        private readonly int _lifetime;

        private Meteor(double x, double y, int direction, int length, int lifetime)
        {
            _x = x;
            _direction = direction;
            _y = y;
            _length = length;
            _lifetime = lifetime;
        }

        public static Meteor Spawn(Random random, int width, int height)
        {
            // Левая звезда летит влево, правая — вправо.
            var direction = random.Next(2) == 0 ? -1 : 1;
            var offset = random.NextDouble() * 0.18;
            return new Meteor(
                x: width * (direction < 0 ? 0.38 - offset : 0.62 + offset),
                y: random.NextDouble() * Math.Max(1, height * 0.35),
                direction: direction,
                length: 6 + random.Next(6),
                lifetime: 12 + random.Next(8));
        }

        public void Advance()
        {
            _x += StepX * _direction;
            _y += StepY;
            _age++;
        }

        public bool IsFinished(int width, int height) =>
            _age > _lifetime || _x + _length < 0 || _x - _length >= width || _y - _length * 0.5 >= height;

        public void Draw(View view, List<System.Drawing.Rectangle> occupied)
        {
            // Под конец жизни звезда гаснет целиком.
            var fade = Math.Clamp((_lifetime - _age) / 5.0, 0, 1);
            for (var k = _length; k >= 0; k--)
            {
                var x = (int)Math.Round(_x - k * _direction);
                var y = (int)Math.Round(_y - k * 0.5);
                if (x < 0 || y < 0 || x >= view.Viewport.Width || y >= view.Viewport.Height || IsOccupied(occupied, x, y))
                    continue;

                var tail = k / (double)_length;
                var glyph = k == 0 ? '✦' : tail < 0.4 ? '─' : '·';
                var bright = Theme.Blend(Theme.Accent, StarColor, 1 - tail);
                var color = Theme.Blend(Theme.Backdrop, bright, (1 - tail * 0.85) * fade);

                view.SetAttribute(new Attribute(color, Color.None));
                view.AddRune(x, y, new System.Text.Rune(glyph));
            }
        }
    }
}
