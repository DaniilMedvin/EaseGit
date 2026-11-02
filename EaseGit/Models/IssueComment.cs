namespace EaseGit.Models;

/// <summary>Комментарий к issue.</summary>
public sealed record IssueComment(string Author, DateTimeOffset CreatedAt, string Body);
