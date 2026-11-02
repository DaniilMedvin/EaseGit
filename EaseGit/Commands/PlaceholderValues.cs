namespace EaseGit.Commands;

/// <summary>Что выбрано на экране — значения подстановок своей команды; null — сейчас нет (файл не выбран и т.п.).</summary>
/// <param name="Repo">Корень репозитория.</param>
/// <param name="File">Полный путь выбранного файла.</param>
/// <param name="Dir">Папка выбранного файла или выбранная папка (полный путь).</param>
/// <param name="Commit">Полный хеш выбранного коммита.</param>
/// <param name="Branch">Выбранная ветка.</param>
/// <param name="Current">Текущая ветка.</param>
public sealed record PlaceholderValues(string Repo, string? File = null, string? Dir = null, string? Commit = null, string? Branch = null,
    string? Current = null);
