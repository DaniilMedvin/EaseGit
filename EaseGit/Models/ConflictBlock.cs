namespace EaseGit.Models;

/// <summary>
/// Блок <c>&lt;&lt;&lt;&lt;&lt;&lt;&lt; наша … ||||||| база … ======= … &gt;&gt;&gt;&gt;&gt;&gt;&gt; их</c>.
/// Строки — с переводами строк, маркеры хранятся целиком, чтобы блок можно было записать обратно как был.
/// </summary>
/// <param name="Base">Общая версия (merge.conflictStyle = diff3 / zdiff3) или null.</param>
public sealed record ConflictBlock(
    string OursMarker, IReadOnlyList<string> Ours,
    string? BaseMarker, IReadOnlyList<string>? Base,
    string Separator, IReadOnlyList<string> Theirs, string TheirsMarker) : ConflictPart
{
    /// <summary>«HEAD» из «&lt;&lt;&lt;&lt;&lt;&lt;&lt; HEAD».</summary>
    public string OursLabel => OursMarker[7..].Trim();

    public string TheirsLabel => TheirsMarker[7..].Trim();
}
