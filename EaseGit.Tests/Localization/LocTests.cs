using System.Globalization;
using EaseGit.Localization;
using EaseGit.Models;
using EaseGit.UI;

namespace EaseGit.Tests.Localization;

/// <summary>Тесты меняют общий язык, поэтому идут отдельно от остальных: те ждут русские тексты.</summary>
[CollectionDefinition(nameof(LocTests), DisableParallelization = true)]
public sealed class LocCollection;

[Collection(nameof(LocTests))]
public sealed class LocTests : IDisposable
{
    public void Dispose() => Loc.Apply(Language.Ru);

    [Theory]
    [InlineData("ru", "en-US", Language.Ru)]
    [InlineData("EN", "ru-RU", Language.En)]
    [InlineData(null, "ru-RU", Language.Ru)]
    [InlineData(null, "de-DE", Language.En)]
    [InlineData("fr", "ru-RU", Language.Ru)]
    [InlineData(null, "", Language.En)]
    public void Resolve_ConfigOrSystem_PicksLanguage(string? configured, string culture, Language expected)
    {
        Assert.Equal(expected, Loc.Resolve(configured, new CultureInfo(culture)));
    }

    [Fact]
    public void T_English_ReturnsSecond()
    {
        Loc.Apply(Language.En);

        Assert.Equal("Commit", Loc.T("Коммит", "Commit"));
    }

    [Theory]
    [InlineData(1, "1 коммит", "1 commit")]
    [InlineData(3, "3 коммита", "3 commits")]
    [InlineData(11, "11 коммитов", "11 commits")]
    [InlineData(21, "21 коммит", "21 commits")]
    public void Plural_BothLanguages_UseOwnRules(int count, string ru, string en)
    {
        Assert.Equal(ru, Loc.Plural(count, "коммит", "коммита", "коммитов", "commit", "commits"));
        Loc.Apply(Language.En);
        Assert.Equal(en, Loc.Plural(count, "коммит", "коммита", "коммитов", "commit", "commits"));
    }

    [Fact]
    public void GitErrors_English_ExplainsInEnglish()
    {
        Loc.Apply(Language.En);

        var text = GitErrors.Explain(new EaseGit.Git.GitResult("git push", 1, "", " ! [rejected]        main -> main (fetch first)"));

        Assert.Equal("Push rejected: the server has new commits. Press p to pull", text);
    }

    [Fact]
    public void CommitTemplates_English_NamesFollowLanguage()
    {
        Loc.Apply(Language.En);

        Assert.Equal("New feature", CommitTemplate.BuiltIn[0].Name);
    }
}
