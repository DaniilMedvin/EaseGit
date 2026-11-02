using System.Reflection;
using Terminal.Gui.Drawing;

namespace EaseGit.UI;

/// <summary>
/// Набор цветов темы. Значения по умолчанию — тёмная тема (стандартная тема Neovim). Основной цвет текста
/// не входит в палитру: он берётся из терминала (Color.None), поэтому светлая тема рассчитана на светлый терминал.
/// Имена свойств (без учёта регистра) — это ключи пользовательской темы в config.json.
/// </summary>
public sealed record ThemePalette
{
    public Color Muted { get; init; } = new("#9B9EA4");
    public Color Dim { get; init; } = new("#4F5258");
    public Color Accent { get; init; } = new("#A6DBFF");
    public Color Green { get; init; } = new("#B3F6C0");
    public Color Cyan { get; init; } = new("#8CF8F7");
    public Color SelectionBg { get; init; } = new("#2C2E33");
    public Color Error { get; init; } = new("#FFC0B9");
    public Color Warning { get; init; } = new("#FCE094");

    /// <summary>Фон добавленных и удалённых строк в diff.</summary>
    public Color AddedBg { get; init; } = new("#1D2F25");
    public Color RemovedBg { get; init; } = new("#3A2427");

    /// <summary>Предполагаемый фон терминала: от него считаются приглушённые фоны плашек и цвет текста на кнопках.</summary>
    public Color Backdrop { get; init; } = new("#14161B");

    public Color SyntaxPlain { get; init; } = new("#DCDCDC");
    public Color SyntaxKeyword { get; init; } = new("#569CD6");
    public Color SyntaxControlKeyword { get; init; } = new("#D8A0DF");
    public Color SyntaxClass { get; init; } = new("#4EC9B0");
    public Color SyntaxStruct { get; init; } = new("#86C691");
    public Color SyntaxInterface { get; init; } = new("#B8D7A3");
    public Color SyntaxMethod { get; init; } = new("#DCDCAA");
    public Color SyntaxVariable { get; init; } = new("#9CDCFE");
    public Color SyntaxString { get; init; } = new("#D69D85");
    public Color SyntaxNumber { get; init; } = new("#B5CEA8");
    public Color SyntaxComment { get; init; } = new("#57A64A");
    public Color SyntaxPreprocessor { get; init; } = new("#9B9B9B");
    public Color SyntaxDisabled { get; init; } = new("#767676");
    public Color SyntaxPunctuation { get; init; } = new("#B4B4B4");

    public static ThemePalette Dark { get; } = new();

    /// <summary>Светлая тема для светлого терминала; подсветка кода — как в светлой теме Visual Studio.</summary>
    public static ThemePalette Light { get; } = new()
    {
        Muted = new("#5F6368"),
        Dim = new("#A0A4AB"),
        Accent = new("#0B6BCB"),
        Green = new("#1A7F37"),
        Cyan = new("#0A7B83"),
        SelectionBg = new("#E3E8EF"),
        Error = new("#CF222E"),
        Warning = new("#9A6700"),
        AddedBg = new("#DAFBE1"),
        RemovedBg = new("#FFEBE9"),
        Backdrop = new("#F8F9FB"),
        SyntaxPlain = new("#1E1E1E"),
        SyntaxKeyword = new("#0000FF"),
        SyntaxControlKeyword = new("#8F08C4"),
        SyntaxClass = new("#2B91AF"),
        SyntaxStruct = new("#2B91AF"),
        SyntaxInterface = new("#2B91AF"),
        SyntaxMethod = new("#74531F"),
        SyntaxVariable = new("#1F377F"),
        SyntaxString = new("#A31515"),
        SyntaxNumber = new("#09885A"),
        SyntaxComment = new("#008000"),
        SyntaxPreprocessor = new("#808080"),
        SyntaxDisabled = new("#808080"),
        SyntaxPunctuation = new("#1E1E1E"),
    };

    /// <summary>Имена цветов, которые можно переопределить в пользовательской теме.</summary>
    public static IReadOnlyList<string> ColorNames { get; } =
        ColorProperties.Select(p => p.Name).ToList();

    private static IEnumerable<PropertyInfo> ColorProperties =>
        typeof(ThemePalette).GetProperties().Where(p => p.PropertyType == typeof(Color));

    /// <summary>
    /// Копия палитры с переопределёнными цветами «имя → #RRGGBB». Неизвестные имена и неверные цвета
    /// пропускаются, их описание попадает в <paramref name="problems"/>: опечатка не должна ломать запуск.
    /// </summary>
    public ThemePalette With(IReadOnlyDictionary<string, string> overrides, ICollection<string>? problems = null)
    {
        var copy = this with { };
        foreach (var (name, value) in overrides)
        {
            var property = ColorProperties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                problems?.Add(T($"неизвестный цвет «{name}»", $"unknown color '{name}'"));
                continue;
            }

            if (!TryParseColor(value, out var color))
            {
                problems?.Add(T($"«{name}»: цвет «{value}» не распознан, нужен вид #RRGGBB", $"'{name}': color '{value}' not recognized, expected #RRGGBB"));
                continue;
            }

            property.SetValue(copy, color);
        }

        return copy;
    }

    /// <summary>Только «#RRGGBB»: именованные цвета и короткая запись зависят от терминала и путают.</summary>
    public static bool TryParseColor(string? text, out Color color)
    {
        color = default;
        if (text is not { Length: 7 } || text[0] != '#' || !text.Skip(1).All(Uri.IsHexDigit))
            return false;

        color = new Color(
            Convert.ToInt32(text[1..3], 16),
            Convert.ToInt32(text[3..5], 16),
            Convert.ToInt32(text[5..7], 16));
        return true;
    }
}
