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

    /// <summary>Имя темы: «dark», «light» или ключ из <see cref="CustomThemes"/>. Не задано — dark.</summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Пользовательские темы: имя → цвета «#RRGGBB» (имена из ThemePalette) и необязательный ключ «base»
    /// («dark» или «light») — какую тему брать за основу.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? CustomThemes { get; init; }

    /// <summary>Свои клавиши: id команды («files.stage») → клавиша («s», «ctrl+x», «F5»).</summary>
    public Dictionary<string, string>? Keys { get; init; }
}
