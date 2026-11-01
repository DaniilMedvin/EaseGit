namespace LazyGit.Models;

/// <summary>Issue на GitHub. Описание и комментарии — только у загруженного целиком.</summary>
public sealed record Issue(GitHubRepo Repo, int Number, string Title, string Author, string Url, DateTimeOffset UpdatedAt)
{
    public IReadOnlyList<string> Labels { get; init; } = [];

    public IReadOnlyList<string> Assignees { get; init; } = [];

    public int CommentCount { get; init; }

    /// <summary>OPEN или CLOSED.</summary>
    public string State { get; init; } = "OPEN";

    public string? Body { get; init; }

    public DateTimeOffset? CreatedAt { get; init; }

    public IReadOnlyList<IssueComment> Comments { get; init; } = [];
}
