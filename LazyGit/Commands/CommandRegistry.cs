using Terminal.Gui.Input;

namespace LazyGit.Commands;

/// <summary>Все команды экрана в порядке, в котором они показываются в подсказках и справке.</summary>
public sealed class CommandRegistry
{
    private readonly List<AppCommand> _commands = [];

    public IReadOnlyList<AppCommand> All => _commands;

    public void Add(AppCommand command)
    {
        if (_commands.Any(c => c.Id == command.Id))
            throw new ArgumentException($"Команда {command.Id} уже зарегистрирована", nameof(command));
        _commands.Add(command);
    }

    /// <summary>
    /// Доступная команда для клавиши: сначала из панели в фокусе, затем глобальная.
    /// Так space в файлах — stage, а в ветках — checkout.
    /// </summary>
    public AppCommand? Find(CommandScope focused, Key key) =>
        _commands.FirstOrDefault(c => c.Scope == focused && c.Key.Matches(key) && c.IsAvailable())
        ?? _commands.FirstOrDefault(c => c.Scope == CommandScope.Global && c.Key.Matches(key) && c.IsAvailable());

    /// <summary>Команды для строки подсказок: сначала панели в фокусе, затем глобальные.</summary>
    public IEnumerable<AppCommand> Hints(CommandScope focused) =>
        _commands.Where(c => c.Hint is not null && c.IsAvailable() && c.Scope == focused)
            .Concat(_commands.Where(c => c.Hint is not null && c.IsAvailable() && c.Scope == CommandScope.Global));
}
