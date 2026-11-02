namespace EaseGit.Models;

/// <summary>Комментарий в обсуждении строки PR.</summary>
public sealed record ReviewComment(string Author, DateTimeOffset CreatedAt, string Body);
