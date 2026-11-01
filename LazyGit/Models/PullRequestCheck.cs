namespace LazyGit.Models;

/// <summary>Одна проверка CI у PR: job Actions или внешний статус.</summary>
public sealed record PullRequestCheck(string Name, CheckState State);
