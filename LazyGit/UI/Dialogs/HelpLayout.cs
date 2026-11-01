using LazyGit.Commands;

namespace LazyGit.UI.Dialogs;

/// <summary>Раскладка клавиш справки: одна или две колонки в зависимости от того, помещаются ли названия.</summary>
public static class HelpLayout
{
    /// <summary>Отступ слева и ширина колонки клавиши. Вместе с названием это ширина ячейки.</summary>
    public const int Indent = 2;

    public const int KeyColumn = 11;

    /// <summary>Ширина одной ячейки: отступ + клавиша + название.</summary>
    public static int CellWidth(AppCommand command) => Indent + KeyColumn + command.Title.Length;

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
