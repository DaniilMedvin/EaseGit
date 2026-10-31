namespace LazyGit.Config;

public sealed record AppConfig
{
    /// <summary>Окно входа пропущено и больше не показывается.</summary>
    public bool AuthPromptSkipped { get; init; }

    public string? LastRepoPath { get; init; }

    /// <summary>Маскот в углу панели diff, выбирается в окне настроек.</summary>
    public Mascot Mascot { get; init; }

    /// <summary>Панель Command Log под diff открыта (переключается клавишей ~).</summary>
    public bool ShowCommandLog { get; init; }
}
