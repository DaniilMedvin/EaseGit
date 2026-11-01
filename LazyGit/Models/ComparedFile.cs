namespace LazyGit.Models;

/// <summary>Файл в сравнении двух веток или коммитов.</summary>
/// <param name="Status">A — добавлен, M — изменён, D — удалён, R — переименован, C — скопирован, T — сменился тип.</param>
/// <param name="OldPath">Прежнее имя у переименования или копии.</param>
/// <param name="Added">Добавлено строк; null — бинарный файл.</param>
/// <param name="Deleted">Удалено строк; null — бинарный файл.</param>
public sealed record ComparedFile(char Status, string Path, string? OldPath, int? Added, int? Deleted);
