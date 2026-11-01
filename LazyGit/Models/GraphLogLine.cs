namespace LazyGit.Models;

/// <summary>Строка git log --graph: псевдографика слева и содержимое по виду строки.</summary>
/// <param name="Graph">Граф git как есть: «* », «| », «|\ », «| * ».</param>
/// <param name="Text">Commit — заголовок, Author — имя автора, Body — строка тела, Graph — пусто.</param>
public sealed record GraphLogLine(GraphLogLineKind Kind, string Graph, string Text = "")
{
    /// <summary>Короткий хеш (Commit).</summary>
    public string? ShortHash { get; init; }

    /// <summary>У коммита больше одного родителя (Commit).</summary>
    public bool IsMerge { get; init; }

    /// <summary>Ветки, теги и HEAD на коммите (Commit).</summary>
    public IReadOnlyList<CommitRef> Refs { get; init; } = [];

    /// <summary>Дата автора (Author).</summary>
    public DateTimeOffset Date { get; init; }
}
