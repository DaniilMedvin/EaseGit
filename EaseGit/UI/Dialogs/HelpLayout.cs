using EaseGit.Commands;

namespace EaseGit.UI.Dialogs;

/// <summary>Раскладка клавиш справки: одна или две колонки в зависимости от того, помещаются ли названия.</summary>
public static class HelpLayout
{
    /// <summary>Отступ слева и ширина колонки клавиши. Вместе с названием это ширина ячейки.</summary>
    public const int Indent = 2;

    public const int KeyColumn = 11;

    /// <summary>Ширина одной ячейки: отступ + клавиша + название.</summary>
    public static int CellWidth(AppCommand command) => Indent + KeyColumn + Title(command).Length;

    /// <summary>Видна ли команда в справке: доступная или недоступная с пояснением.</summary>
    public static bool IsShown(AppCommand command) => command.UnavailableNote is not null || command.IsAvailable();

    /// <summary>Название в справке: у недоступной команды к нему добавляется пояснение.</summary>
    public static string Title(AppCommand command) =>
        command.UnavailableNote is { } note && !command.IsAvailable() ? $"{command.Title} — {note}" : command.Title;

    /// <summary>
    /// Строки раздела. Если названия всех команд помещаются в половину ширины (<paramref name="columnWidth"/>),
    /// команды идут по две в строке, иначе по одной: длинное название не должно залезать на вторую колонку.
    /// </summary>
    public static IReadOnlyList<(AppCommand First, AppCommand? Second)> Arrange(IReadOnlyList<AppCommand> commands, int columnWidth)
    {
        var twoColumns = commands.All(c => CellWidth(c) <= columnWidth);
        var rows = new List<(AppCommand, AppCommand?)>();
        for (var i = 0; i < commands.Count; i += twoColumns ? 2 : 1)
            rows.Add((commands[i], twoColumns && i + 1 < commands.Count ? commands[i + 1] : null));
        return rows;
    }
}
