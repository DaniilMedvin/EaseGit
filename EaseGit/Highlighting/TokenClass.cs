namespace EaseGit.Highlighting;

/// <summary>Класс токена для подсветки синтаксиса. Цвета задаются в <see cref="UI.Theme.Syntax"/>.</summary>
public enum TokenClass
{
    Plain,
    Keyword,
    ControlKeyword,
    Class,
    Struct,
    Interface,
    Enum,
    TypeParameter,
    Method,
    Variable,
    String,
    Number,
    Comment,
    Preprocessor,
    DisabledText,
    Punctuation,
}

/// <summary>Фрагмент текста с классом токена. Start и Length — в символах.</summary>
public readonly record struct HighlightSpan(int Start, int Length, TokenClass Class)
{
    public int End => Start + Length;
}
