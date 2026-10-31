namespace LazyGit.Models;

/// <summary>Запись reflog HEAD.</summary>
/// <param name="Hash">Куда указал HEAD после действия.</param>
/// <param name="Subject">Описание действия: «commit: feat: add a», «checkout: moving from main to f».</param>
public sealed record ReflogEntry(string Hash, string Subject);
