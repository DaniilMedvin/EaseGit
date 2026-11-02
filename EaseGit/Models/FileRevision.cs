namespace EaseGit.Models;

/// <summary>Коммит из истории файла и что в нём случилось с файлом.</summary>
/// <param name="Status">A — добавлен, M — изменён, D — удалён, R — переименован, C — скопирован, T — сменился тип.</param>
/// <param name="Path">Имя файла в этом коммите (после переименования — новое).</param>
/// <param name="OldPath">Прежнее имя у переименования или копии, иначе null.</param>
public sealed record FileRevision(Commit Commit, char Status, string Path, string? OldPath = null);
