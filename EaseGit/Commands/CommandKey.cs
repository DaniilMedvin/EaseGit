using Terminal.Gui.Input;

namespace EaseGit.Commands;

/// <summary>Клавиша команды: как она подписана в подсказках и как её узнать в событии клавиатуры.</summary>
public sealed class CommandKey
{
    private readonly Func<Key, bool> _matches;

    private CommandKey(string label, Func<Key, bool> matches)
    {
        Label = label;
        _matches = matches;
    }

    /// <summary>Подпись для подсказок: «space», «ctrl+p», «D».</summary>
    public string Label { get; }

    public bool Matches(Key key) => _matches(key);

    public static CommandKey Of(string label, params Key[] keys) => new(label, key => keys.Any(k => k == key));

    /// <summary>Клавиша-символ: «?», «,», «~», «:». Сравнивается по введённому символу, а не по коду клавиши.</summary>
    public static CommandKey Char(char c) => new(c.ToString(), key => key.AsRune.Value == c);

    /// <summary>Несколько вариантов одной команды с общей подписью: «? / F1».</summary>
    public static CommandKey Any(string label, params CommandKey[] variants) =>
        new(label, key => variants.Any(v => v.Matches(key)));
}
