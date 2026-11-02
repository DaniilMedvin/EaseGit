namespace EaseGit.Models;

/// <summary>Файл с конфликтом, разобранный на части. <paramref name="HasBom"/> — при записи BOM сохраняется.</summary>
public sealed record ConflictFile(string Path, IReadOnlyList<ConflictPart> Parts, bool HasBom)
{
    public int Blocks => Parts.Count(p => p is ConflictBlock);
}
