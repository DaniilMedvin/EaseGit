namespace LazyGit.Models;

/// <summary>Коммит из истории.</summary>
/// <param name="Hash">Полный хеш.</param>
/// <param name="AuthorName">Имя автора.</param>
/// <param name="AuthorEmail">Email автора.</param>
/// <param name="Date">Дата создания коммита автором.</param>
/// <param name="Subject">Первая строка сообщения.</param>
public sealed record Commit(string Hash, string AuthorName, string AuthorEmail, DateTimeOffset Date, string Subject)
{
    public string ShortHash => Hash.Length > 7 ? Hash[..7] : Hash;
}
