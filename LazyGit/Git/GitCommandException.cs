namespace LazyGit.Git;

/// <summary>Git-команда, которая должна была выполниться успешно, завершилась с ошибкой.</summary>
public sealed class GitCommandException(GitResult result)
    : Exception($"{result.Command} завершилась с кодом {result.ExitCode}: {result.StdErr.Trim()}")
{
    public GitResult Result { get; } = result;
}
