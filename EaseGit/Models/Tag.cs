namespace EaseGit.Models;

/// <summary>Тег: имя, коммит, на который он указывает, и подпись.</summary>
/// <param name="Hash">Полный хеш коммита (у аннотированного — коммита, а не объекта тега).</param>
/// <param name="Annotated">Аннотированный (с сообщением и автором) или лёгкий.</param>
/// <param name="Subject">У аннотированного — сообщение тега, у лёгкого — заголовок коммита.</param>
public sealed record Tag(string Name, string Hash, bool Annotated, DateTimeOffset Date, string Subject)
{
    public string ShortHash => Hash[..Math.Min(7, Hash.Length)];
}
