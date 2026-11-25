namespace EaseGit.UI;

/// <summary>
/// Выбор символов под шрифт терминала. Windows Terminal, VS Code и терминалы Linux/macOS
/// берут недостающие символы из других шрифтов, а классическая консоль Windows — нет:
/// там вместо ◉, ⎇, ✓ или ⠋ видны пустые прямоугольники, поэтому для неё нужны замены из Consolas.
/// </summary>
public static class ConsoleGlyphs
{
    private static readonly Func<string, bool>? Check = Detect();

    /// <summary>Рисует ли шрифт терминала все символы строки. Если шрифт неизвестен — считаем, что да.</summary>
    public static bool Supports(string text) => Check is null || Check(text);

    /// <summary><paramref name="preferred"/>, если шрифт его рисует, иначе <paramref name="fallback"/>.</summary>
    public static string Pick(string preferred, string fallback) => Choose(preferred, fallback, Check);

    public static char Pick(char preferred, char fallback) => Choose(preferred.ToString(), fallback.ToString(), Check)[0];

    /// <param name="supports">Проверка шрифта; null — шрифт неизвестен, берём основной символ.</param>
    public static string Choose(string preferred, string fallback, Func<string, bool>? supports) =>
        supports is null || supports(preferred) ? preferred : fallback;

    private static Func<string, bool>? Detect()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        // Терминалы с подстановкой шрифтов: у них GetCurrentConsoleFontEx отвечает про скрытую консоль, а не про их шрифт.
        string[] modernTerminals = ["WT_SESSION", "TERM_PROGRAM", "ConEmuPID", "ALACRITTY_WINDOW_ID", "WEZTERM_PANE"];
        if (modernTerminals.Any(name => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(name))))
            return null;

        try
        {
            return ConsoleFont.TryCreateGlyphCheck();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }
}
