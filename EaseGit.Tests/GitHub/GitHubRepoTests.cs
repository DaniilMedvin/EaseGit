using EaseGit.Models;

namespace EaseGit.Tests.GitHub;

public sealed class GitHubRepoTests
{
    [Theory]
    [InlineData("https://github.com/DaniilMedvin/EaseGit.git")]
    [InlineData("https://github.com/DaniilMedvin/EaseGit")]
    [InlineData("git@github.com:DaniilMedvin/EaseGit.git")]
    [InlineData("ssh://git@github.com/DaniilMedvin/EaseGit.git")]
    [InlineData("  https://GitHub.com/DaniilMedvin/EaseGit/  ")]
    public void FromRemoteUrl_GitHubFormats_ReturnsOwnerAndName(string url)
    {
        Assert.Equal(new GitHubRepo("DaniilMedvin", "EaseGit"), GitHubRepo.FromRemoteUrl(url));
    }

    [Theory]
    [InlineData("https://gitlab.com/a/b.git")]
    [InlineData("/home/me/repos/bare.git")]
    [InlineData("https://github.com/only-owner")]
    public void FromRemoteUrl_NotGitHub_ReturnsNull(string url)
    {
        Assert.Null(GitHubRepo.FromRemoteUrl(url));
    }

    [Fact]
    public void FromWebUrl_PullRequestUrl_ReturnsRepo()
    {
        Assert.Equal("cli/cli", GitHubRepo.FromWebUrl("https://github.com/cli/cli/pull/14846")?.NameWithOwner);
    }
}
