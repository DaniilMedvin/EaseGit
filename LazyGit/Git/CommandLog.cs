namespace LazyGit.Git;

/// <summary>Выполненная git-команда.</summary>
/// <param name="Args">Аргументы без «git».</param>
/// <param name="ExitCode">Код выхода или null, если команда отменена.</param>
/// <param name="Output">stderr и stdout вместе, без пустых строк по краям.</param>
public sealed record CommandLogEntry(DateTimeOffset Time, IReadOnlyList<string> Args, int? ExitCode, TimeSpan Duration, string Output)
{
    public bool Success => ExitCode == 0;

    public bool Cancelled => ExitCode is null;
}

/// <summary>
/// Журнал команд, выполненных по действиям пользователя (stage, commit, push, …). Фоновые чтения сюда не пишутся.
/// Хранит последние <see cref="Capacity"/> записей. Потокобезопасен: команды завершаются в фоновых потоках.
/// </summary>
public sealed class CommandLog
{
    public const int Capacity = 200;

    private readonly List<CommandLogEntry> _entries = [];
    private readonly Lock _lock = new();

    /// <summary>Добавлена запись. Вызывается в потоке, где завершилась команда.</summary>
    public event EventHandler? Changed;

    /// <summary>Снимок записей, старые первыми.</summary>
    public IReadOnlyList<CommandLogEntry> Entries
    {
        get
        {
            lock (_lock)
                return _entries.ToList();
        }
    }

    public void Add(CommandLogEntry entry)
    {
        lock (_lock)
        {
            _entries.Add(entry);
            if (_entries.Count > Capacity)
                _entries.RemoveAt(0);
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
