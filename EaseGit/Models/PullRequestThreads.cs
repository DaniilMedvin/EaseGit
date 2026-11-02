namespace EaseGit.Models;

/// <summary>Обсуждения строк PR и последний коммит ветки PR: к нему привязываются новые комментарии.</summary>
public sealed record PullRequestThreads(string HeadSha, IReadOnlyList<ReviewThread> Threads);
