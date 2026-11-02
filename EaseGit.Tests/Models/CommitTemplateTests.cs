using EaseGit.Models;

namespace EaseGit.Tests.Models;

public class CommitTemplateTests
{
    [Fact]
    public void BuiltIn_TypesAreKnownToCommitMessage()
    {
        foreach (var template in CommitTemplate.BuiltIn.Where(t => t.Type is not null))
            Assert.Contains(template.Type, CommitMessage.Types);
    }

    [Fact]
    public void BuiltIn_NamesAreUnique()
    {
        var names = CommitTemplate.BuiltIn.Select(t => t.Name).ToList();

        Assert.Equal(names.Count, names.Distinct().Count());
    }

    [Theory]
    [InlineData("Новая функция", "feat: add ")]
    [InlineData("Ломающее изменение", "feat!: ")]
    [InlineData("Добавить README", "docs: add README")]
    public void Title_CombinesTypeAndSubject(string name, string expected)
    {
        Assert.Equal(expected, CommitTemplate.BuiltIn.Single(t => t.Name == name).Title);
    }

    [Fact]
    public void BreakingTemplate_ComposesValidConventionalTitleOnceCompleted()
    {
        var template = CommitTemplate.BuiltIn.Single(t => t.Name == "Ломающее изменение");

        var message = CommitMessage.Compose(template.Type, template.Subject + "drop old api", template.Body + "removed v1");

        Assert.Equal("feat!: drop old api\n\nBREAKING CHANGE: removed v1", message);
    }
}
