namespace LazyGit.Models;

/// <summary>Локальная ветка.</summary>
/// <param name="Name">Имя ветки, например main или feature/diff.</param>
/// <param name="IsCurrent">Ветка, на которой сейчас находится HEAD.</param>
/// <param name="Upstream">Upstream-ветка или null.</param>
/// <param name="Ahead">Количество коммитов, которых нет в upstream.</param>
/// <param name="Behind">Количество коммитов upstream, которых нет локально.</param>
public sealed record Branch(string Name, bool IsCurrent, string? Upstream, int Ahead, int Behind);
