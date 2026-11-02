using EaseGit.GitHub;
using EaseGit.Models;

namespace EaseGit.Tests.GitHub;

public sealed class RepoChoicesTests
{
    private static readonly IReadOnlyList<RepoChoice> Remote = RepoChoices.ParseList("""
        [{"description":"TUI для git","isPrivate":false,"nameWithOwner":"DaniilMedvin/EaseGit"},
         {"description":"Сайт по продаже цветов","isPrivate":true,"nameWithOwner":"DaniilMedvin/TopFlowers"},
         {"description":null,"isPrivate":false,"nameWithOwner":"DaniilMedvin/PZ1"}]
        """);

    [Fact]
    public void ParseList_ReadsNameDescriptionAndPrivacy()
    {
        Assert.Equal(3, Remote.Count);
        Assert.True(Remote[1].IsPrivate);
        Assert.Equal("Сайт по продаже цветов", Remote[1].Description);
        Assert.Equal("", Remote[2].Description);
    }

    [Fact]
    public void Merge_LocalsFirstWithPathAndRemoteInfo_NoDuplicates()
    {
        var merged = RepoChoices.Merge(
            [(new GitHubRepo("cli", "cli"), "/src/cli"), (new GitHubRepo("daniilmedvin", "easegit"), "/src/EaseGit")], Remote);

        Assert.Equal(["cli/cli", "daniilmedvin/easegit", "DaniilMedvin/TopFlowers", "DaniilMedvin/PZ1"], merged.Select(c => c.Repo.NameWithOwner));
        Assert.Equal("/src/EaseGit", merged[1].LocalPath);
        Assert.Equal("TUI для git", merged[1].Description);
        Assert.Null(merged[2].LocalPath);
    }

    [Fact]
    public void Filter_ByNameOrDescriptionIgnoringCase()
    {
        Assert.Equal(["DaniilMedvin/TopFlowers"], RepoChoices.Filter(Remote, "цвет").Select(c => c.Repo.NameWithOwner));
        Assert.Equal(["DaniilMedvin/EaseGit"], RepoChoices.Filter(Remote, "ease").Select(c => c.Repo.NameWithOwner));
        Assert.Equal(3, RepoChoices.Filter(Remote, "  ").Count);
    }

    [Fact]
    public void Filter_OwnerSlashRepoNotInList_AddsManualFirst()
    {
        var found = RepoChoices.Filter(Remote, "cli/go-gh");

        var manual = Assert.Single(found);
        Assert.True(manual.Manual);
        Assert.Equal(new GitHubRepo("cli", "go-gh"), manual.Repo);
        Assert.DoesNotContain(RepoChoices.Filter(Remote, "daniilmedvin/easegit"), c => c.Manual);
        Assert.DoesNotContain(RepoChoices.Filter(Remote, "cli/"), c => c.Manual);
    }
}
