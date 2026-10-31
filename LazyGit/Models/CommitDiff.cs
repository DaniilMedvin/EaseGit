namespace LazyGit.Models;

/// <summary>Содержимое коммита: полное сообщение и изменения всех файлов.</summary>
/// <param name="Message">Сообщение коммита целиком: заголовок, пустая строка, тело.</param>
/// <param name="Lines">Diff всех файлов подряд, каждый файл начинается со строки «diff --git».</param>
public sealed record CommitDiff(string Message, IReadOnlyList<DiffLine> Lines);
