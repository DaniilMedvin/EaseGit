namespace LazyGit.Models;

/// <summary>
/// Pull request. Поиск по всем репозиториям отдаёт не все поля: ветки, проверки и ревью тогда пусты,
/// они приходят при загрузке PR целиком.
/// </summary>
/// <param name="Repo">Репозиторий PR.</param>
/// <param name="Author">Логин автора.</param>
public sealed record PullRequest(GitHubRepo Repo, int Number, string Title, string Author, string Url, DateTimeOffset UpdatedAt)
{
    public bool IsDraft { get; init; }

    /// <summary>Ветка с изменениями.</summary>
    public string? HeadRef { get; init; }

    /// <summary>Ветка, в которую PR вливается.</summary>
    public string? BaseRef { get; init; }

    public ReviewState Review { get; init; }

    /// <summary>Итог проверок CI.</summary>
    public CheckState Checks { get; init; }

    public IReadOnlyList<PullRequestCheck> CheckRuns { get; init; } = [];

    public int? Additions { get; init; }

    public int? Deletions { get; init; }

    /// <summary>Описание PR в Markdown (только у загруженного целиком).</summary>
    public string? Body { get; init; }

    /// <summary>MERGEABLE, CONFLICTING или UNKNOWN (только у загруженного целиком).</summary>
    public string? Mergeable { get; init; }
}
