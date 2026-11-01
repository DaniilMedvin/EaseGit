namespace LazyGit.Models;

public enum RefKind
{
    /// <summary>HEAD без ветки (detached).</summary>
    Head,
    Branch,
    Remote,
    Tag,
}

/// <summary>Ссылка, указывающая на коммит: ветка, удалённая ветка, тег или HEAD.</summary>
/// <param name="Name">Короткое имя: main, origin/main, v1.0.</param>
/// <param name="IsCurrent">Текущая ветка (HEAD -> main).</param>
public sealed record CommitRef(string Name, RefKind Kind, bool IsCurrent = false);
