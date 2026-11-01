using LazyGit.Models;

namespace LazyGit.UI.Dialogs;

/// <summary>Откуда окно коммита берёт issues для ссылки: репозиторий и загрузка его открытых issues.</summary>
public sealed record IssueSource(GitHubRepo Repo, Func<CancellationToken, Task<IReadOnlyList<Issue>>> Load);
