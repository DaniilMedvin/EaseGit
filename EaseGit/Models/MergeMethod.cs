namespace EaseGit.Models;

/// <summary>Как влить PR на GitHub.</summary>
public enum MergeMethod
{
    /// <summary>Merge-коммит: вся история ветки сохраняется.</summary>
    Merge,

    /// <summary>Все коммиты PR склеиваются в один.</summary>
    Squash,

    /// <summary>Коммиты PR переносятся поверх основной ветки без merge-коммита.</summary>
    Rebase,
}
