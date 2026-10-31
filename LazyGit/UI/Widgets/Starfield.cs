using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Звёздное небо на фоне: редкие мерцающие звёзды и изредка падающая звезда.
/// Звёзды не рисуются поверх других элементов окна. Располагается в координатах (0, 0) окна.
/// </summary>
public sealed class Starfield : AnimatedView
{
    private const int StarDensity = 120;
    private static readonly char[] StarGlyphs = ['·', '·', '·', '·', '·', '∙', '∙', '⋆'];
    private static readonly Color StarColor = new("#E0E2EA");

    private readonly Random _random = new();
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
        return true;
    }

    private void DrawStars(List<System.Drawing.Rectangle> occupied)
    {
        for (var y = 0; y < Viewport.Height; y++)
        {
            for (var x = 0; x < Viewport.Width; x++)
            {
                var hash = Hash(x, y);
                if (hash % StarDensity != 0 || IsOccupied(occupied, x, y))
                    continue;

                var glyph = StarGlyphs[hash / StarDensity % StarGlyphs.Length];
                var phase = hash % 628 / 100.0;
                var speed = 0.6 + hash % 5 * 0.35;
                var twinkle = (Math.Sin(Seconds * speed + phase) + 1) / 2;
                var color = Theme.Blend(Theme.Backdrop, StarColor, 0.15 + 0.45 * twinkle);

                SetAttribute(new Attribute(color, Color.None));
                AddRune(x, y, new System.Text.Rune(glyph));
            }
        }
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

    private static bool IsOccupied(List<System.Drawing.Rectangle> occupied, int x, int y)
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

    /// <summary>Падающая звезда: летит влево-вниз, хвост постепенно гаснет.</summary>
    private sealed class Meteor
    {
        private const double StepX = -1.6;
        private const double StepY = 0.8;

        private double _x;
        private double _y;
        private int _age;
        private readonly int _length;
        private readonly int _lifetime;

        private Meteor(double x, double y, int length, int lifetime)
        {
            _x = x;
            _y = y;
            _length = length;
            _lifetime = lifetime;
        }

        public static Meteor Spawn(Random random, int width, int height) => new(
            x: width * (0.35 + random.NextDouble() * 0.6),
            y: random.NextDouble() * Math.Max(1, height * 0.35),
            length: 6 + random.Next(6),
            lifetime: 14 + random.Next(10));

        public void Advance()
        {
            _x += StepX;
            _y += StepY;
            _age++;
        }

        public bool IsFinished(int width, int height) =>
            _age > _lifetime || _x + _length < 0 || _y - _length * 0.5 >= height;

        public void Draw(View view, List<System.Drawing.Rectangle> occupied)
        {
            // Под конец жизни звезда гаснет целиком.
            var fade = Math.Clamp((_lifetime - _age) / 5.0, 0, 1);
            for (var k = _length; k >= 0; k--)
            {
                var x = (int)Math.Round(_x + k);
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
