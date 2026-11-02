namespace EaseGit.Models;

/// <summary>Где искать в истории.</summary>
public enum HistorySearchMode
{
    /// <summary>В сообщении коммита (git log --grep).</summary>
    Message,

    /// <summary>В имени или почте автора (git log --author).</summary>
    Author,

    /// <summary>Коммиты, где строка появилась в коде или исчезла (git log -S).</summary>
    Code,
}
