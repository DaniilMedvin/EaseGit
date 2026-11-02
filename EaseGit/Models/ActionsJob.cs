namespace EaseGit.Models;

/// <summary>Job запуска со своими шагами.</summary>
public sealed record ActionsJob(string Name, CheckState State, string Conclusion, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt,
    string Url, IReadOnlyList<ActionsStep> Steps)
{
    public TimeSpan? Duration => StartedAt is { } start && CompletedAt is { } end && end > start ? end - start : null;
}
