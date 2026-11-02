namespace EaseGit.Models;

/// <summary>Вид строки вывода git log --graph.</summary>
public enum GraphLogLineKind
{
    /// <summary>Коммит: хеш, ссылки, заголовок.</summary>
    Commit,

    /// <summary>Автор и дата коммита.</summary>
    Author,

    /// <summary>Строка тела сообщения.</summary>
    Body,

    /// <summary>Только граф: соединения веток или пустая строка между коммитами.</summary>
    Graph,
}
