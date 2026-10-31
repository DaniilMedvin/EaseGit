using System.Text.RegularExpressions;

namespace LazyGit.Models;

public enum TipSeverity
{
    Info,
    Warning,
    Error,
}

/// <summary>Подсказка о том, как улучшить сообщение коммита.</summary>
public sealed record CommitTip(string Text, TipSeverity Severity);

/// <summary>Сборка и проверка сообщения коммита в формате Conventional Commits.</summary>
public static partial class CommitMessage
{
    public const int RecommendedLength = 50;
    public const int MaxLength = 72;

    /// <summary>Типы коммитов. null — без типа.</summary>
    public static readonly string?[] Types = ["feat", "fix", "docs", "refactor", "test", "chore", "style", null];

    /// <summary>
    /// Итоговый заголовок: «тип: текст». Если текст уже начинается с типа (например, «fix(ui): …»),
    /// он используется как есть.
    /// </summary>
    public static string Compose(string? type, string subject)
    {
        subject = subject.Trim();
        if (type is null || subject.Length == 0 || HasTypePrefix(subject))
            return subject;
        return $"{type}: {subject}";
    }

    public static bool HasTypePrefix(string subject) => TypePrefixRegex().IsMatch(subject);

    /// <summary>Подсказки по заголовку, самые важные первыми.</summary>
    public static IReadOnlyList<CommitTip> Check(string? type, string subject)
    {
        var tips = new List<CommitTip>();
        var text = TypePrefixRegex().Replace(subject.Trim(), "");
        if (text.Length == 0)
            return tips;

        var length = Compose(type, subject).Length;
        if (length > MaxLength)
            tips.Add(new CommitTip($"Заголовок длиннее {MaxLength} символов — сократи его", TipSeverity.Error));
        else if (length > RecommendedLength)
            tips.Add(new CommitTip($"Лучше уложиться в {RecommendedLength} символов", TipSeverity.Warning));

        if (CyrillicRegex().IsMatch(text))
            tips.Add(new CommitTip("Сообщения коммитов обычно пишут на английском", TipSeverity.Info));

        var firstWord = text.Split(' ', 2)[0];
        if (firstWord.Length > 4 && (firstWord.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                     || firstWord.EndsWith("ing", StringComparison.OrdinalIgnoreCase)))
        {
            var verb = firstWord.ToLowerInvariant();
            tips.Add(new CommitTip($"Пиши как команду (add, fix), а не «{verb}»", TipSeverity.Warning));
        }

        if (char.IsUpper(text[0]) && !IsAcronym(firstWord))
            tips.Add(new CommitTip("После типа обычно пишут со строчной буквы", TipSeverity.Info));

        if (text.EndsWith('.'))
            tips.Add(new CommitTip("В конце заголовка точку не ставят", TipSeverity.Info));

        return tips;
    }

    private static bool IsAcronym(string word) => word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));

    [GeneratedRegex(@"^[a-z]+(\([^)]*\))?!?: ")]
    private static partial Regex TypePrefixRegex();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex CyrillicRegex();
}
