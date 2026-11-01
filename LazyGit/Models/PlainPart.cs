namespace LazyGit.Models;

/// <summary>Текст вне конфликта. Строки — вместе со своими переводами строк (\n или \r\n).</summary>
public sealed record PlainPart(IReadOnlyList<string> Lines) : ConflictPart;
