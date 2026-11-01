namespace LazyGit.UI;

/// <summary>Что главному окну сделать после закрытия окна GitHub.</summary>
public enum GitHubAction
{
    /// <summary>Перечитать репозиторий: был checkout ветки PR.</summary>
    Refresh,

    /// <summary>Открыть окно нового PR для текущей ветки.</summary>
    CreatePullRequest,
}
