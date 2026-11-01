namespace LazyGit.Models;

/// <summary>Предложенное сообщение коммита: тип Conventional Commits, заголовок без типа и тело-список.</summary>
public sealed record CommitSuggestion(string? Type, string Subject, string Body);
