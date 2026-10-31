using LazyGit.Config;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Маскоты 11×12 пикселей, рисуются полублоками — 11×6 символов. Узнаваемые упрощённые значки:
/// Claude — оранжевый краб Clawd, ChatGPT — зелёный «узел» из колец, Gemini — четырёхлучевая звезда с градиентом.
/// </summary>
public static class MascotArt
{
    public const int Width = 11;
    public const int Rows = 6;

    // Clawd — краб-маскот Claude Code: квадратное тело с глазами-прорезями, клешни по бокам, четыре ножки.
    private static readonly string[] Claude =
    [
        "...........",
        "...........",
        ".#########.",
        ".#########.",
        ".##.###.##.",
        ".##.###.##.",
        "###########",
        "###########",
        ".#########.",
        ".#########.",
        ".#.#...#.#.",
        ".#.#...#.#.",
    ];

    private static readonly string[] ChatGpt =
    [
        "...#####...",
        "..#.....#..",
        ".#..###..#.",
        "#..#.#.#..#",
        "#.#..#..#.#",
        "#.#######.#",
        "#.#..#..#.#",
        "#..#.#.#..#",
        ".#..###..#.",
        "..#.....#..",
        "...#####...",
        "...........",
    ];

    private static readonly string[] Gemini =
    [
        ".....#.....",
        ".....#.....",
        "....###....",
        "...#####...",
        ".#########.",
        "###########",
        ".#########.",
        "...#####...",
        "....###....",
        ".....#.....",
        ".....#.....",
        "...........",
    ];

    /// <summary>Рисует маскота, левый верхний угол — (x, y).</summary>
    public static void Draw(View view, int x, int y, Mascot mascot)
    {
        if (mascot == Mascot.None)
            return;

        for (var row = 0; row < Rows; row++)
        {
            for (var col = 0; col < Width; col++)
                view.DrawHalfBlock(x + col, y + row, Pixel(mascot, col, row * 2), Pixel(mascot, col, row * 2 + 1));
        }
    }

    public static string Name(Mascot mascot) => mascot switch
    {
        Mascot.Claude => "Clawd",
        Mascot.ChatGpt => "ChatGPT",
        Mascot.Gemini => "Gemini",
        _ => "Нет",
    };

    private static Color? Pixel(Mascot mascot, int x, int y)
    {
        var grid = mascot switch
        {
            Mascot.Claude => Claude,
            Mascot.ChatGpt => ChatGpt,
            _ => Gemini,
        };
        if (grid[y][x] != '#')
            return null;

        return mascot switch
        {
            Mascot.Claude => Theme.MascotClaude,
            Mascot.ChatGpt => Theme.MascotChatGpt,
            _ => GeminiGradient((x + y) / (double)(Width + 10)),
        };
    }

    /// <summary>Градиент Gemini по диагонали: синий → фиолетовый → розовый.</summary>
    private static Color GeminiGradient(double t)
    {
        var stops = Theme.MascotGemini;
        var scaled = Math.Clamp(t, 0, 1) * (stops.Length - 1);
        var index = Math.Min((int)scaled, stops.Length - 2);
        return Theme.Blend(stops[index], stops[index + 1], scaled - index);
    }
}
