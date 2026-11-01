using Terminal.Gui.Drivers;
using Terminal.Gui.Input;

namespace LazyGit.Commands;

/// <summary>
/// Разбор клавиши из config.json: «s», «S» (shift+s), «ctrl+x», «alt+f», «F5», «space», «enter», «esc», «tab»,
/// «up», «down», «left», «right», «home», «end», «pageup», «pagedown», «delete», «backspace», цифры
/// и символы вроде «?», «~», «,».
/// </summary>
public static class KeyParser
{
    private static readonly Dictionary<string, KeyCode> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["space"] = KeyCode.Space,
        ["enter"] = KeyCode.Enter,
        ["esc"] = KeyCode.Esc,
        ["escape"] = KeyCode.Esc,
        ["tab"] = KeyCode.Tab,
        ["up"] = KeyCode.CursorUp,
        ["down"] = KeyCode.CursorDown,
        ["left"] = KeyCode.CursorLeft,
        ["right"] = KeyCode.CursorRight,
        ["home"] = KeyCode.Home,
        ["end"] = KeyCode.End,
        ["pageup"] = KeyCode.PageUp,
        ["pagedown"] = KeyCode.PageDown,
        ["delete"] = KeyCode.Delete,
        ["backspace"] = KeyCode.Backspace,
    };

    /// <summary>Клавиша или null и причина, если строку разобрать нельзя.</summary>
    public static (CommandKey? Key, string? Error) Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return (null, T("пустая клавиша", "empty key"));

        var rest = text.Trim();
        bool ctrl = false, alt = false, shift = false;
        while (true)
        {
            if (TryStrip(ref rest, "ctrl+"))
                ctrl = true;
            else if (TryStrip(ref rest, "alt+"))
                alt = true;
            else if (TryStrip(ref rest, "shift+"))
                shift = true;
            else
                break;
        }

        if (rest.Length == 0)
            return (null, T($"после модификатора нужна клавиша: «{text}»", $"a key is needed after the modifier: '{text}'"));

        var plain = !ctrl && !alt && !shift;
        KeyCode code;
        if (Named.TryGetValue(rest, out var named))
        {
            code = named;
        }
        else if (rest.Length is 2 or 3 && (rest[0] is 'f' or 'F') && int.TryParse(rest[1..], out var number) && number is >= 1 and <= 12)
        {
            code = Offset(KeyCode.F1, number - 1);
        }
        else if (rest.Length == 1 && char.IsAsciiLetter(rest[0]))
        {
            // Заглавная буква без других модификаторов — shift+буква, как в подсказках («D», «M»).
            if (char.IsAsciiLetterUpper(rest[0]) && !ctrl && !alt)
                shift = true;
            code = Offset(KeyCode.A, char.ToUpperInvariant(rest[0]) - 'A');
        }
        else if (rest.Length == 1 && char.IsAsciiDigit(rest[0]))
        {
            code = Offset(KeyCode.D0, rest[0] - '0');
        }
        else if (rest.Length == 1 && plain && !char.IsControl(rest[0]) && !char.IsWhiteSpace(rest[0]))
        {
            // Символ сравнивается по введённому знаку, а не по коду клавиши: «?» зависит от раскладки.
            return (CommandKey.Char(rest[0]), null);
        }
        else
        {
            return (null, T($"не знаю клавишу «{text}»", $"unknown key '{text}'"));
        }

        var key = new Key(code);
        if (ctrl)
            key = key.WithCtrl;
        if (alt)
            key = key.WithAlt;
        if (shift)
            key = key.WithShift;
        return (CommandKey.Of(Label(rest, ctrl, alt, shift), key), null);
    }

    /// <summary>Коды букв, цифр и F-клавиш идут подряд.</summary>
    private static KeyCode Offset(KeyCode start, int offset) => (KeyCode)((uint)start + (uint)offset);

    private static string Label(string name, bool ctrl, bool alt, bool shift)
    {
        var isLetter = name.Length == 1 && char.IsAsciiLetter(name[0]);
        // «D» вместо «shift+d» — так подписаны и встроенные команды.
        if (isLetter && shift && !ctrl && !alt)
            return name.ToUpperInvariant();

        var main = isLetter ? name.ToLowerInvariant() : name.Length > 1 ? name.ToLowerInvariant() : name;
        return (ctrl ? "ctrl+" : "") + (alt ? "alt+" : "") + (shift ? "shift+" : "") + main;
    }

    private static bool TryStrip(ref string text, string prefix)
    {
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        text = text[prefix.Length..];
        return true;
    }
}
