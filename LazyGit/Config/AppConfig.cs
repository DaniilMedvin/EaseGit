namespace LazyGit.Config;

public sealed record AppConfig
{
    /// <summary>Окно входа пропущено и больше не показывается.</summary>
    public bool AuthPromptSkipped { get; init; }

    public string? LastRepoPath { get; init; }
}
