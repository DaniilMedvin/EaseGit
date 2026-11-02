namespace EaseGit.Config;

public sealed record AppConfig
{
    /// <summary>Окно входа пропущено и больше не показывается.</summary>
    public bool AuthPromptSkipped { get; init; }

    public string? LastRepoPath { get; init; }

    /// <summary>Недавно открытые репозитории, свежие первыми: по ним окно GitHub находит локальную копию проекта PR.</summary>
    public IReadOnlyList<string>? RecentRepos { get; init; }

    public const int RecentReposLimit = 20;

    /// <summary>Запоминает открытый репозиторий: последний и первый в списке недавних, без повторов.</summary>
    public AppConfig RememberRepo(string path) => this with
    {
        LastRepoPath = path,
        RecentRepos = [path, .. (RecentRepos ?? []).Where(p => p != path).Take(RecentReposLimit - 1)],
    };

    /// <summary>Маскот в углу панели diff, выбирается в окне настроек.</summary>
    public Mascot Mascot { get; init; }

    /// <summary>Перенос длинных строк в diff (клавиша w); не задано — включён.</summary>
    public bool? WrapDiff { get; init; }

    /// <summary>Панель Command Log под diff открыта (переключается клавишей ~).</summary>
    public bool ShowCommandLog { get; init; }

    /// <summary>Имя темы: «dark», «light» или ключ из <see cref="CustomThemes"/>. Не задано — dark.</summary>
    public string? Theme { get; init; }

    /// <summary>
    /// Пользовательские темы: имя → цвета «#RRGGBB» (имена из ThemePalette) и необязательный ключ «base»
    /// («dark» или «light») — какую тему брать за основу.
    /// </summary>
    public Dictionary<string, Dictionary<string, string>>? CustomThemes { get; init; }

    /// <summary>Язык интерфейса: «ru» или «en». Не задан — по языку системы.</summary>
    public string? Language { get; init; }

    /// <summary>Фоновый fetch раз в столько минут; не задано — 5, 0 — выключен.</summary>
    public int? AutoFetchMinutes { get; init; }

    public const int DefaultAutoFetchMinutes = 5;

    /// <summary>Свои команды: клавиша → команда shell с подстановками (открыть файл в Rider, скрипт, gh browse).</summary>
    public IReadOnlyList<CustomCommand>? CustomCommands { get; init; }

    /// <summary>Свои клавиши: id команды («files.stage») → клавиша («s», «ctrl+x», «F5»).</summary>
    public Dictionary<string, string>? Keys { get; init; }
}
