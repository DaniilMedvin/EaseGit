namespace LazyGit.Git;

public sealed record GitResult(string Command, int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
}
