using LazyGit.Models;

namespace LazyGit.Tests.Models;

public class CommitMessageTests
{
    [Theory]
    [InlineData("feat", "add diff panel", "feat: add diff panel")]
    [InlineData("fix", "  trim spaces  ", "fix: trim spaces")]
    [InlineData(null, "add diff panel", "add diff panel")]
    [InlineData("feat", "fix(ui): already typed", "fix(ui): already typed")]
    [InlineData("feat", "feat!: breaking change", "feat!: breaking change")]
    [InlineData("feat", "", "")]
    public void Compose_AddsTypeUnlessPresent(string? type, string subject, string expected)
    {
        Assert.Equal(expected, CommitMessage.Compose(type, subject));
    }

    [Fact]
    public void Compose_WithBody_SeparatesWithBlankLine()
    {
        var message = CommitMessage.Compose("feat", "add body", "- first\n- second");

        Assert.Equal("feat: add body\n\n- first\n- second", message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\n\n  \n")]
    public void Compose_EmptyBody_ReturnsTitleOnly(string body)
    {
        Assert.Equal("fix: x", CommitMessage.Compose("fix", "x", body));
    }

    [Fact]
    public void Compose_Body_TrimsTrailingSpacesAndOuterBlankLines()
    {
        var message = CommitMessage.Compose(null, "title", "\n\n- a   \r\n\n- b\n\n");

        Assert.Equal("title\n\n- a\n\n- b", message);
    }

    [Fact]
    public void Compose_EmptySubjectWithBody_ReturnsEmpty()
    {
        Assert.Equal("", CommitMessage.Compose("feat", " ", "- body"));
    }

    [Fact]
    public void CheckBody_LongLine_WarnsWithLineNumber()
    {
        var tip = Assert.Single(CommitMessage.CheckBody("- short\n- " + new string('x', 80)));

        Assert.Equal(TipSeverity.Warning, tip.Severity);
        Assert.Contains("Строка 2", tip.Text);
    }

    [Fact]
    public void CheckBody_ShortLines_HasNoTips()
    {
        Assert.Empty(CommitMessage.CheckBody("- a\n- b"));
    }

    [Fact]
    public void Check_GoodSubject_HasNoTips()
    {
        Assert.Empty(CommitMessage.Check("feat", "add diff panel"));
    }

    [Fact]
    public void Check_EmptySubject_HasNoTips()
    {
        Assert.Empty(CommitMessage.Check("feat", "   "));
    }

    [Theory]
    [InlineData("added diff panel")]
    [InlineData("fixing parser")]
    public void Check_NotImperative_Warns(string subject)
    {
        var tip = Assert.Single(CommitMessage.Check("feat", subject));

        Assert.Equal(TipSeverity.Warning, tip.Severity);
        Assert.Contains("как команду", tip.Text);
    }

    [Fact]
    public void Check_UppercaseAndPeriod_GiveInfoTips()
    {
        var tips = CommitMessage.Check("feat", "Add diff panel.");

        Assert.Contains(tips, t => t.Text.Contains("строчной"));
        Assert.Contains(tips, t => t.Text.Contains("точку"));
    }

    [Fact]
    public void Check_Acronym_IsNotUppercaseTip()
    {
        Assert.Empty(CommitMessage.Check("docs", "API reference"));
    }

    [Fact]
    public void Check_Cyrillic_SuggestsEnglish()
    {
        Assert.Contains(CommitMessage.Check("feat", "добавить панель"), t => t.Text.Contains("английском"));
    }

    [Theory]
    [InlineData(45, null)]
    [InlineData(60, TipSeverity.Warning)]
    [InlineData(80, TipSeverity.Error)]
    public void Check_Length_WarnsAndErrors(int totalLength, TipSeverity? expected)
    {
        var subject = "add " + new string('x', totalLength - "feat: add ".Length);

        var lengthTip = CommitMessage.Check("feat", subject).FirstOrDefault(t => t.Text.Contains("символов"));

        Assert.Equal(expected, lengthTip?.Severity);
    }
}
