using LazyGit.Config;
using LazyGit.GitHub;
using LazyGit.Models;

namespace LazyGit.Tests.GitHub;

public sealed class LocalClonesTests
{
    [Fact]
    public void RememberRepo_MovesToFrontWithoutDuplicatesAndLimit()
    {
        var config = new AppConfig { RecentRepos = [.. Enumerable.Range(0, AppConfig.RecentReposLimit).Select(i => $"/p{i}")] };

        var updated = config.RememberRepo("/p5");

        Assert.Equal("/p5", updated.LastRepoPath);
        Assert.Equal("/p5", updated.RecentRepos![0]);
        Assert.Single(updated.RecentRepos, p => p == "/p5");
        Assert.Equal(AppConfig.RecentReposLimit, updated.RecentRepos.Count);
        Assert.Equal(AppConfig.RecentReposLimit, config.RememberRepo("/new").RecentRepos!.Count);
    }

    [Fact]
    public async Task FindAsync_RecentRepoWithSameRemote_IgnoringCase()
    {
        var other = new TempRepository();
        var clone = new TempRepository();
        await other.InitializeAsync();
        await clone.InitializeAsync();
        try
        {
            await other.RunAsync("remote", "add", "origin", "https://github.com/someone/else.git");
            await clone.RunAsync("remote", "add", "origin", "git@github.com:DaniilMedvin/LazyGit.git");

            var found = await LocalClones.FindAsync(clone.Git, ["/no/such/dir", other.Path, clone.Path], new GitHubRepo("daniilmedvin", "lazygit"));

            Assert.Equal(clone.Path, found);
            Assert.Null(await LocalClones.FindAsync(clone.Git, [other.Path], new GitHubRepo("DaniilMedvin", "LazyGit")));
        }
        finally
        {
            await other.DisposeAsync();
            await clone.DisposeAsync();
        }
    }

    [Fact]
    public void ClonePath_NextToProjectOrWithOwnerWhenTaken()
    {
        var parent = Directory.CreateTempSubdirectory("lgit-clone-").FullName;
        try
        {
            var project = Path.Combine(parent, "LazyGit");
            Assert.Equal(Path.Combine(parent, "cli"), LocalClones.ClonePath(project, new GitHubRepo("cli", "cli")));

            Directory.CreateDirectory(Path.Combine(parent, "cli"));
            Assert.Equal(Path.Combine(parent, "cli-cli"), LocalClones.ClonePath(project, new GitHubRepo("cli", "cli")));
        }
        finally
        {
            Directory.Delete(parent, recursive: true);
        }
    }
}
