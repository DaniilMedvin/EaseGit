using LazyGit.Models;

namespace LazyGit.Tests.GitHub;

public sealed class GitHubRepoTests
{
    [Theory]
    [InlineData("https://github.com/DaniilMedvin/LazyGit.git")]
    [InlineData("https://github.com/DaniilMedvin/LazyGit")]
    [InlineData("git@github.com:DaniilMedvin/LazyGit.git")]
    [InlineData("ssh://git@github.com/DaniilMedvin/LazyGit.git")]
    [InlineData("  https://GitHub.com/DaniilMedvin/LazyGit/  ")]
    public void FromRemoteUrl_GitHubFormats_ReturnsOwnerAndName(string url)
    {
        Assert.Equal(new GitHubRepo("DaniilMedvin", "LazyGit"), GitHubRepo.FromRemoteUrl(url));
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
