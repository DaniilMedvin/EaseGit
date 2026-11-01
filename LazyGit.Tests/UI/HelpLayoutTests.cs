using LazyGit.Commands;
using LazyGit.UI.Dialogs;
using Terminal.Gui.Input;

namespace LazyGit.Tests.UI;

public class HelpLayoutTests
{
    private const int Column = 37;

    private static AppCommand Command(string title) => new()
    {
        Id = title,
        Title = title,
        Scope = CommandScope.Global,
        Key = CommandKey.Of("x", Key.X),
        Execute = () => { },
    };

    [Fact]
    public void Arrange_ShortTitles_PairsCommandsInTwoColumns()
    {
        var rows = HelpLayout.Arrange([Command("Обновить"), Command("Pull"), Command("Fetch")], Column);

        Assert.Equal(2, rows.Count);
        Assert.NotNull(rows[0].Second);
        Assert.Null(rows[1].Second);
    }

    [Fact]
    public void Arrange_OneLongTitle_UsesSingleColumnForWholeSection()
    {
        var rows = HelpLayout.Arrange([Command("Обновить"), Command("Отменить последнее действие"), Command("Pull")], Column);

        Assert.Equal(3, rows.Count);
        Assert.All(rows, r => Assert.Null(r.Second));
    }

    [Fact]
    public void Arrange_TitleExactlyFitting_StaysInTwoColumns()
    {
        var fits = Command(new string('я', Column - HelpLayout.Indent - HelpLayout.KeyColumn));

        var rows = HelpLayout.Arrange([fits, Command("a")], Column);

        Assert.Single(rows);
    }

    [Fact]
    public void Arrange_Empty_ReturnsNoRows()
    {
        Assert.Empty(HelpLayout.Arrange([], Column));
    }
}
