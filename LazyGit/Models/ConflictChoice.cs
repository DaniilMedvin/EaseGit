namespace LazyGit.Models;

/// <summary>Что оставить в блоке конфликта.</summary>
public enum ConflictChoice
{
    Ours,
    Theirs,

    /// <summary>Сначала наши строки, затем их.</summary>
    Both,
}
