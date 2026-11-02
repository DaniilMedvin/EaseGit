using System.Text.RegularExpressions;

namespace EaseGit.Commands;

/// <summary>
/// Подстановки в своей команде: {file}, {dir}, {commit}, {branch}, {current}, {repo}. Значение берётся в кавычки shell,
/// поэтому пробелы, кириллица и кавычки в путях команду не ломают. Прочие фигурные скобки (${HOME}, {a,b}) не трогаются.
/// </summary>
public static partial class CommandTemplate
{
    /// <summary>Команда с подстановками или имя подстановки, для которой сейчас нет значения.</summary>
    public static (string? Command, string? Missing) Expand(string template, PlaceholderValues context, bool windows)
    {
        string? missing = null;
        var command = PlaceholderRegex().Replace(template, match =>
        {
            var name = match.Groups[1].Value;
            var value = name switch
            {
                "file" => context.File,
                "dir" => context.Dir,
                "commit" => context.Commit,
                "branch" => context.Branch,
                "current" => context.Current,
                _ => context.Repo,
            };
            if (value is null)
            {
                missing ??= name;
                return match.Value;
            }

            return Quote(value, windows);
        });
        return missing is null ? (command, null) : (null, missing);
    }

    /// <summary>Значение одним аргументом: в sh — в одинарных кавычках ('it'\''s'), в cmd — в двойных.</summary>
    public static string Quote(string value, bool windows) =>
        windows ? $"\"{value.Replace("\"", "\"\"")}\"" : $"'{value.Replace("'", "'\\''")}'";

    [GeneratedRegex(@"\{(file|dir|commit|branch|current|repo)\}")]
    private static partial Regex PlaceholderRegex();
}
