namespace LazyGit.Models;

/// <summary>
/// Ветка, влитая в основную не merge-коммитом, а копиями: rebase (каждый коммит скопирован) или squash (все одним).
/// Сама ветка указывает на старые коммиты, поэтому на графе её линия обрывается.
/// </summary>
/// <param name="Branch">Имя ветки: «feature/x» или «origin/feature/x».</param>
/// <param name="Base">Куда влита: «main» или «origin/main».</param>
/// <param name="Commit">Коммит в основной ветке с этими изменениями (у rebase — копия вершины ветки).</param>
/// <param name="Squashed">Влита одним коммитом (squash), иначе — rebase.</param>
public sealed record MergedCopy(string Branch, string Base, string Commit, bool Squashed)
{
    public string ShortCommit => Commit[..Math.Min(7, Commit.Length)];
}
