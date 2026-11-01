namespace LazyGit.Models;

/// <summary>Коммит из blame: кто и когда, заголовок и имя файла в этом коммите.</summary>
/// <param name="Path">Имя файла в этом коммите (до переименования — старое).</param>
/// <param name="PreviousHash">Родитель, где у строк была прошлая версия (для «что было до этого»), или null.</param>
/// <param name="PreviousPath">Имя файла в <paramref name="PreviousHash"/>.</param>
public sealed record BlameCommit(string Hash, string Author, string AuthorEmail, DateTimeOffset Date, string Summary, string Path,
    string? PreviousHash, string? PreviousPath)
{
    /// <summary>Строки, ещё не закоммиченные: git пишет нулевой хеш.</summary>
    public bool IsUncommitted => Hash.All(c => c == '0');

    public string ShortHash => Hash[..Math.Min(7, Hash.Length)];

    public Commit ToCommit() => new(Hash, Author, AuthorEmail, Date, Summary);
}
