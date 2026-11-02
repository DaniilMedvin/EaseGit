namespace EaseGit.UI;

/// <summary>Что главному окну сделать после закрытия окна GitHub.</summary>
public enum GitHubAction
{
    /// <summary>Перечитать репозиторий: был checkout ветки PR.</summary>
    Refresh,

    /// <summary>Открыть окно нового PR для текущей ветки.</summary>
    CreatePullRequest,

    /// <summary>Открыть другой репозиторий: его путь в <see cref="GitHubWindow.OpenPath"/>.</summary>
    OpenRepository,

    /// <summary>
    /// PR этого репозитория влит на GitHub (<see cref="GitHubWindow.MergedPullRequest"/>): fetch и перемотать
    /// локальную базовую ветку.
    /// </summary>
    Merged,
}
