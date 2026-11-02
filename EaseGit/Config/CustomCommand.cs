namespace EaseGit.Config;

/// <summary>
/// Своя команда из config.json: клавиша → команда shell с подстановками {file}, {dir}, {commit}, {branch}, {current}, {repo}.
/// </summary>
/// <remarks>Поля необязательные: без key или command команда не регистрируется, а в строке статуса — пояснение.</remarks>
public sealed record CustomCommand
{
    /// <summary>Клавиша, как в разделе keys: «ctrl+r», «F6», «R».</summary>
    public string? Key { get; init; }

    /// <summary>Название в палитре, справке и подсказке; не задано — сама команда.</summary>
    public string? Title { get; init; }

    /// <summary>Команда для sh -c (на Windows — cmd /c), запускается в корне репозитория.</summary>
    public string? Command { get; init; }

    /// <summary>Где работает: global (по умолчанию), files, commits, branches, stash.</summary>
    public string? Scope { get; init; }

    /// <summary>Показать вывод в окне по мере выполнения; иначе — в фоне, итог в строке статуса.</summary>
    public bool Output { get; init; }

    /// <summary>Перед запуском — подтверждение с командой после подстановок.</summary>
    public bool Confirm { get; init; }
}
