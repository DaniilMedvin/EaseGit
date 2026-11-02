using System.Globalization;

namespace EaseGit.Localization;

public enum Language
{
    Ru,
    En,
}

/// <summary>
/// Язык интерфейса. Строки пишутся парой прямо в коде: <c>T("Коммит", "Commit")</c> — перевод рядом с местом,
/// где он показывается, интерполяция остаётся читаемой, а забытый перевод не скомпилируется.
/// Язык задаётся один раз до создания окон (как тема) и меняется только с перезапуском окна.
/// </summary>
public static class Loc
{
    public static Language Current { get; private set; } = Language.Ru;

    public static void Apply(Language language) => Current = language;

    /// <summary>Строка на текущем языке.</summary>
    public static string T(string ru, string en) => Current == Language.En ? en : ru;

    /// <summary>
    /// Язык из конфига («ru» / «en», без учёта регистра); не задан или неизвестен — по языку системы:
    /// русский для ru, иначе английский.
    /// </summary>
    public static Language Resolve(string? configured, CultureInfo system) =>
        configured?.Trim().ToLowerInvariant() switch
        {
            "ru" => Language.Ru,
            "en" => Language.En,
            _ => system.TwoLetterISOLanguageName == "ru" ? Language.Ru : Language.En,
        };

    /// <summary>Код языка для конфига и сообщений: «ru», «en».</summary>
    public static string Code(Language language) => language.ToString().ToLowerInvariant();

    /// <summary>«5 коммитов» / «5 commits» — число со словом в нужной форме на текущем языке.</summary>
    public static string Plural(int count, string one, string few, string many, string enOne, string enMany) =>
        Current == Language.En ? PluralEn(count, enOne, enMany) : PluralRu(count, one, few, many);

    /// <summary>Русские формы: 1 коммит, 2 коммита, 5 коммитов, 11 коммитов, 21 коммит.</summary>
    public static string PluralRu(int count, string one, string few, string many)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = mod10 == 1 && mod100 != 11 ? one
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few
            : many;
        return $"{count} {word}";
    }

    public static string PluralEn(int count, string one, string many) => $"{count} {(count == 1 ? one : many)}";
}
