namespace EaseGit.Models;

/// <summary>Запуск GitHub Actions в списке: workflow, коммит, ветка, состояние.</summary>
/// <param name="Conclusion">success, failure, cancelled, skipped… или пусто, пока идёт.</param>
public sealed record ActionsRun(long Id, int Number, string Workflow, string Title, string Branch, string Sha, string Event,
    CheckState State, string Conclusion, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, string Url)
{
    public string ShortSha => Sha[..Math.Min(7, Sha.Length)];
}
