namespace LazyGit.Models;

/// <summary>Проверка имени новой ветки по правилам git check-ref-format, с понятными подсказками.</summary>
public static class BranchName
{
    private const string ForbiddenChars = "~^:?*[\\";

    /// <summary>Текст ошибки или null, если имя подходит.</summary>
    public static string? Check(string name, IEnumerable<string> existing)
    {
        if (name.Length == 0)
            return T("Введи имя ветки, например feature/branches", "Enter a branch name, e.g. feature/branches");
        if (name.Any(char.IsWhiteSpace))
            return T("Пробелы нельзя: используй - или /", "No spaces: use - or /");
        if (name.Any(c => char.IsControl(c) || ForbiddenChars.Contains(c)))
            return T("Нельзя символы ~ ^ : ? * [ \\", "Characters ~ ^ : ? * [ \\ are not allowed");
        if (name.Contains("..") || name.Contains("@{") || name == "@")
            return T("Нельзя «..», «@{» и имя «@»", "'..', '@{' and the name '@' are not allowed");
        if (name.StartsWith('-'))
            return T("Имя не может начинаться с -", "The name can't start with -");
        if (name.StartsWith('/') || name.EndsWith('/') || name.Contains("//"))
            return T("Лишний / в начале, в конце или два подряд", "Extra / at the start, at the end or twice in a row");

        foreach (var part in name.Split('/'))
        {
            if (part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(".lock", StringComparison.Ordinal))
                return T("Часть имени не может начинаться с точки или заканчиваться на . и .lock", "A name part can't start with a dot or end with . or .lock");
        }

        if (existing.Contains(name, StringComparer.Ordinal))
            return T($"Ветка {name} уже существует", $"Branch {name} already exists");
        return null;
    }
}
