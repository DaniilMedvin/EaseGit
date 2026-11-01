namespace LazyGit.UI;

/// <summary>Вкладка окна GitHub.</summary>
public enum GitHubTab
{
    /// <summary>Открытые PR текущего репозитория.</summary>
    Repository,

    /// <summary>Мои открытые PR во всех репозиториях.</summary>
    Mine,

    /// <summary>PR, где ждут моего ревью.</summary>
    ReviewRequested,

    /// <summary>PR репозитория, выбранного через «/».</summary>
    Other,

    /// <summary>Открытые issues репозитория (выбранного через «/» или текущего).</summary>
    Issues,
}
