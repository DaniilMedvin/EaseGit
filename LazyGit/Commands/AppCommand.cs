namespace LazyGit.Commands;

/// <summary>Где работает команда: в панели в фокусе или везде.</summary>
public enum CommandScope
{
    Global,
    Files,
    Branches,
    Commits,
    Stash,

    /// <summary>Diff в режиме построчного stage.</summary>
    Diff,
}

/// <summary>
/// Действие пользователя. Из одного описания строятся обработка клавиш, строка подсказок,
/// раздел «Клавиши» справки и палитра команд.
/// </summary>
public sealed class AppCommand
{
    /// <summary>Стабильный идентификатор: «files.stage».</summary>
    public required string Id { get; init; }

    /// <summary>Название в справке и палитре: «Stage / unstage файла».</summary>
    public required string Title { get; init; }

    public required CommandScope Scope { get; init; }

    public required CommandKey Key { get; init; }

    public required Action Execute { get; init; }

    /// <summary>Короткая подпись в строке подсказок («stage») или null — команды там нет.</summary>
    public string? Hint { get; init; }

    /// <summary>Подсказка второстепенная: убирается первой, если строка не помещается.</summary>
    public bool HintOptional { get; init; }

    /// <summary>Можно ли выполнить сейчас. Недоступная команда не ловит клавишу и не видна в палитре.</summary>
    public Func<bool> IsAvailable { get; init; } = () => true;

    /// <summary>
    /// Пояснение, почему команда сейчас недоступна («уже есть»). Если задано, недоступная команда
    /// всё равно видна в справке (серым, с пояснением), чтобы о функции можно было узнать.
    /// </summary>
    public string? UnavailableNote { get; init; }

    /// <summary>Git-команда, которая будет выполнена, — для палитры. null — не git-команда.</summary>
    public Func<string?>? Preview { get; init; }

    /// <summary>Необратимое действие: в палитре отмечено «⚠», выполняется через подтверждение.</summary>
    public bool IsDangerous { get; init; }

    /// <summary>Дополнительные слова для поиска в палитре: «мерж слить» для Merge.</summary>
    public string Keywords { get; init; } = "";

    /// <summary>Скрыта из палитры (навигация вроде tab или ctrl+d/u: искать её незачем).</summary>
    public bool HiddenInPalette { get; init; }
}
