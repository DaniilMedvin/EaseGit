namespace LazyGit.Models;

/// <summary>Запуск GitHub Actions для коммита.</summary>
/// <param name="Id">Номер запуска для gh run view.</param>
public sealed record WorkflowRun(long Id, string Name, CheckState State, string Url);
