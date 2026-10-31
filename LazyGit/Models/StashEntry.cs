namespace LazyGit.Models;

/// <summary>Запись stash.</summary>
/// <param name="Ref">Ссылка для команд git: «stash@{0}». Номер меняется, когда удаляются записи выше.</param>
/// <param name="Hash">Хеш коммита stash: по нему выбор сохраняется после обновления списка.</param>
/// <param name="Message">Сообщение: своё («my message») или «WIP on main: 7949d52 init».</param>
/// <param name="Branch">Ветка, на которой сделан stash, или null.</param>
public sealed record StashEntry(string Ref, string Hash, string AuthorName, string AuthorEmail, DateTimeOffset Date, string Message, string? Branch)
{
    /// <summary>Номер записи: 0 — последняя.</summary>
    public int Index => int.TryParse(Ref.AsSpan(7, Math.Max(0, Ref.Length - 8)), out var index) ? index : 0;
}
