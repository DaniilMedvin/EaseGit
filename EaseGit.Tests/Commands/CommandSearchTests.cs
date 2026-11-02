using EaseGit.Commands;
using Terminal.Gui.Input;

namespace EaseGit.Tests.Commands;

public class CommandSearchTests
{
    private static AppCommand Command(string title, string keywords = "") => new()
    {
        Id = title.ToLowerInvariant(),
        Title = title,
        Scope = CommandScope.Global,
        Key = CommandKey.Of("x", Key.X),
        Execute = () => { },
        Keywords = keywords,
    };

    [Theory]
    [InlineData("pus", "Push", true)]
    [InlineData("PSH", "Push", true)]
    [InlineData("hsup", "Push", false)]
    [InlineData("", "Push", true)]
    public void Score_LettersInOrder_Match(string query, string text, bool matches)
    {
        Assert.Equal(matches, CommandSearch.Score(query, text) is not null);
    }

    [Fact]
    public void Score_ContiguousAndWordStart_RankHigher()
    {
        Assert.True(CommandSearch.Score("pu", "Push") > CommandSearch.Score("pu", "Open status"));
    }

    [Fact]
    public void Filter_BestFirst_KeepsOrderOnTies()
    {
        var commands = new[] { Command("Pull"), Command("Open status"), Command("Push") };

        Assert.Equal(["Pull", "Push", "Open status"], CommandSearch.Filter(commands, "pu").Select(c => c.Title));
    }

    [Fact]
    public void Filter_RussianKeyword_FindsLatinTitle()
    {
        var commands = new[] { Command("Merge в текущую", "мерж слить"), Command("Pull") };

        Assert.Equal(["Merge в текущую"], CommandSearch.Filter(commands, "мерж").Select(c => c.Title));
    }
}
