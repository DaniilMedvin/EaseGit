namespace LazyGit.Models;

/// <summary>Проверка имени новой ветки по правилам git check-ref-format, с понятными подсказками.</summary>
public static class BranchName
{
    private const string ForbiddenChars = "~^:?*[\\";

    /// <summary>Текст ошибки или null, если имя подходит.</summary>
    public static string? Check(string name, IEnumerable<string> existing)
    {
        if (name.Length == 0)
            return "Введи имя ветки, например feature/branches";
        if (name.Any(char.IsWhiteSpace))
            return "Пробелы нельзя: используй - или /";
        if (name.Any(c => char.IsControl(c) || ForbiddenChars.Contains(c)))
            return "Нельзя символы ~ ^ : ? * [ \\";
        if (name.Contains("..") || name.Contains("@{") || name == "@")
            return "Нельзя «..», «@{» и имя «@»";
        if (name.StartsWith('-'))
            return "Имя не может начинаться с -";
        if (name.StartsWith('/') || name.EndsWith('/') || name.Contains("//"))
            return "Лишний / в начале, в конце или два подряд";

        foreach (var part in name.Split('/'))
        {
            if (part.StartsWith('.') || part.EndsWith('.') || part.EndsWith(".lock", StringComparison.Ordinal))
                return "Часть имени не может начинаться с точки или заканчиваться на . и .lock";
        }

        if (existing.Contains(name, StringComparer.Ordinal))
            return $"Ветка {name} уже существует";
        return null;
    }
}
