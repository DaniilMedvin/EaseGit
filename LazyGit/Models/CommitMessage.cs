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

    /// <summary>
    /// Полное сообщение: заголовок, пустая строка и тело. Пустые строки в конце тела и пробелы
    /// в конце строк убираются; пустое тело не добавляется.
    /// </summary>
    public static string Compose(string? type, string subject, string body)
    {
        var title = Compose(type, subject);
        var lines = body.ReplaceLineEndings("\n").Split('\n').Select(l => l.TrimEnd()).ToList();
        while (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        while (lines.Count > 0 && lines[0].Length == 0)
            lines.RemoveAt(0);

        return lines.Count == 0 || title.Length == 0 ? title : $"{title}\n\n{string.Join('\n', lines)}";
    }

    /// <summary>Подсказки по телу коммита.</summary>
    /// <summary>
    /// Дописывает в конец тела ссылку на issue: «Fixes #12» для типа fix, иначе «Closes #12» — GitHub закроет issue,
    /// когда коммит попадёт в основную ветку. Ссылка встаёт отдельным абзацем, к уже стоящим ссылкам — без пустой строки;
    /// если такая уже есть, тело не меняется.
    /// </summary>
    public static string LinkIssue(string body, string? type, int number)
    {
        var lines = body.ReplaceLineEndings("\n").TrimEnd().Split('\n').ToList();
        if (lines.Any(l => IssueLinkRegex().Match(l) is { Success: true } m && m.Groups[1].Value == number.ToString()))
            return body;

        var link = $"{(type == "fix" ? "Fixes" : "Closes")} #{number}";
        if (lines is [""])
            return link;
        return IssueLinkRegex().IsMatch(lines[^1])
            ? $"{string.Join('\n', lines)}\n{link}"
            : $"{string.Join('\n', lines)}\n\n{link}";
    }

    public static IReadOnlyList<CommitTip> CheckBody(string body)
    {
        var tips = new List<CommitTip>();
        var lines = body.ReplaceLineEndings("\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].TrimEnd().Length > MaxLength)
            {
                tips.Add(new CommitTip(T($"Строка {i + 1} тела длиннее {MaxLength} символов — перенеси её", $"Body line {i + 1} is longer than {MaxLength} characters, wrap it"), TipSeverity.Warning));
                break;
            }
        }

        return tips;
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
            tips.Add(new CommitTip(T($"Заголовок длиннее {MaxLength} символов — сократи его", $"The subject is longer than {MaxLength} characters, shorten it"), TipSeverity.Error));
        else if (length > RecommendedLength)
            tips.Add(new CommitTip(T($"Лучше уложиться в {RecommendedLength} символов", $"Better keep it within {RecommendedLength} characters"), TipSeverity.Warning));

        if (CyrillicRegex().IsMatch(text))
            tips.Add(new CommitTip(T("Сообщения коммитов обычно пишут на английском", "Commit messages are usually written in English"), TipSeverity.Info));

        var firstWord = text.Split(' ', 2)[0];
        if (firstWord.Length > 4 && (firstWord.EndsWith("ed", StringComparison.OrdinalIgnoreCase)
                                     || firstWord.EndsWith("ing", StringComparison.OrdinalIgnoreCase)))
        {
            var verb = firstWord.ToLowerInvariant();
            tips.Add(new CommitTip(T($"Пиши как команду (add, fix), а не «{verb}»", $"Write it as a command (add, fix), not '{verb}'"), TipSeverity.Warning));
        }

        if (char.IsUpper(text[0]) && !IsAcronym(firstWord))
            tips.Add(new CommitTip(T("После типа обычно пишут со строчной буквы", "After the type, start with a lowercase letter"), TipSeverity.Info));

        if (text.EndsWith('.'))
            tips.Add(new CommitTip(T("В конце заголовка точку не ставят", "No period at the end of the subject"), TipSeverity.Info));

        return tips;
    }

    private static bool IsAcronym(string word) => word.Length > 1 && word.All(c => !char.IsLetter(c) || char.IsUpper(c));

    [GeneratedRegex(@"^[a-z]+(\([^)]*\))?!?: ")]
    private static partial Regex TypePrefixRegex();

    [GeneratedRegex(@"\p{IsCyrillic}")]
    private static partial Regex CyrillicRegex();

    /// <summary>Строка-ссылка на issue: «Fixes #12», «closes #3», «Resolved #7».</summary>
    [GeneratedRegex(@"^\s*(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s+#(\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex IssueLinkRegex();
}
