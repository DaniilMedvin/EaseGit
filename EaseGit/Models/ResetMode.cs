namespace EaseGit.Models;

/// <summary>Режим <c>git reset</c>: что происходит с индексом и рабочей копией при переносе ветки на другой коммит.</summary>
public enum ResetMode
{
    /// <summary>Только ветка: изменения коммитов остаются в индексе.</summary>
    Soft,

    /// <summary>Ветка и индекс: изменения остаются в рабочей копии, но не в индексе.</summary>
    Mixed,

    /// <summary>Ветка, индекс и рабочая копия: все изменения теряются.</summary>
    Hard,
}
