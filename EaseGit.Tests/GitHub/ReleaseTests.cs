using EaseGit.Git;
using EaseGit.GitHub;
using EaseGit.Models;

namespace EaseGit.Tests.GitHub;

public sealed class ReleaseTests : IAsyncLifetime
{
    private static readonly GitHubRepo Repo = new("o", "r");

    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private static Commit Commit(string subject, string hash, int parents = 1) =>
        new(hash.PadRight(40, '0'), "a", "a@x", DateTimeOffset.Now, subject) { Parents = Enumerable.Repeat("p", parents).ToList() };

    [Fact]
    public void Build_GroupsByTypeOldestFirstAndSkipsMerges()
    {
        // Как отдаёт git log: новые первыми.
        IReadOnlyList<Commit> commits =
        [
            Commit("Merge pull request #3 from o/x", "eeeeeee", parents: 2),
            Commit("feat!: drop the old config format", "ddddddd"),
            Commit("docs: update README", "ccccccc"),
            Commit("fix(ui): cursor jumps after reload", "bbbbbbb"),
            Commit("feat: review comments in the diff", "aaaaaaa"),
            Commit("feat(tags): releases from tags", "9999999"),
        ];

        var notes = ReleaseNotes.Build(commits, Repo, "v1.1.0", "v1.0.0");

        Assert.Equal("""
            ### Breaking changes

            - drop the old config format (ddddddd)

            ### Features

            - **tags:** releases from tags (9999999)
            - review comments in the diff (aaaaaaa)

            ### Fixes

            - **ui:** cursor jumps after reload (bbbbbbb)

            ### Other changes

            - update README (ccccccc)

            **Full changelog**: https://github.com/o/r/compare/v1.0.0...v1.1.0
            """.ReplaceLineEndings("\n"), notes);
    }

    [Fact]
    public void Build_FirstTagWithoutConventionalCommits_ListsOtherChangesWithoutCompareLink()
    {
        var notes = ReleaseNotes.Build([Commit("Initial import", "1234567")], Repo, "v0.1.0", previousTag: null);

        Assert.Equal("### Other changes\n\n- Initial import (1234567)", notes);
    }

    [Theory]
    [InlineData("v1.2.0-beta.1", true)]
    [InlineData("2.0.0-rc1", true)]
    [InlineData("v1.2.0", false)]
    [InlineData("release-2026", false)]
    public void IsPrerelease_SemVerSuffix(string tag, bool expected) => Assert.Equal(expected, ReleaseNotes.IsPrerelease(tag));

    [Fact]
    public void ParseList_ReadsFlags()
    {
        var releases = ReleaseParser.ParseList("""
            [{"isDraft":false,"isLatest":true,"isPrerelease":false,"name":"EaseGit v1.0.0","tagName":"v1.0.0"},
             {"isDraft":true,"isLatest":false,"isPrerelease":true,"name":"","tagName":"v1.1.0-rc1"}]
            """);

        Assert.Equal(new GitHubRelease("v1.0.0", "EaseGit v1.0.0", false, false, true), releases[0]);
        Assert.True(releases[1].IsDraft);
        Assert.True(releases[1].IsPrerelease);
    }

    [Fact]
    public void CreateReleaseArguments_VerifiesTagAndTakesNotesFromStdinOrGitHub()
    {
        Assert.Equal(["release", "create", "v1", "--repo", "o/r", "--verify-tag", "--title", "v1", "--notes-file", "-"],
            GitHubService.CreateReleaseArguments(Repo, "v1", "v1", draft: false, prerelease: false, generateNotes: false));
        Assert.Equal(["release", "create", "v1-rc", "--repo", "o/r", "--verify-tag", "--title", "RC", "--generate-notes", "--draft", "--prerelease"],
            GitHubService.CreateReleaseArguments(Repo, "v1-rc", "RC", draft: true, prerelease: true, generateNotes: true));
    }

    [Fact]
    public async Task ReleaseCommits_SincePreviousTag()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("feat: first");
        await _repo.RunAsync("tag", "v1.0.0");
        _repo.Write("a.txt", "2");
        await _repo.CommitAsync("fix: second");
        _repo.Write("a.txt", "3");
        await _repo.CommitAsync("feat: third");
        await _repo.RunAsync("tag", "--annotate", "v1.1.0", "--message", "Релиз");
        var tags = await _service.GetTagsAsync();
        var first = tags.Single(t => t.Name == "v1.0.0");
        var second = tags.Single(t => t.Name == "v1.1.0");

        Assert.Null(await _service.GetPreviousTagAsync(first));
        Assert.Equal("v1.0.0", await _service.GetPreviousTagAsync(second));
        Assert.Equal(["feat: third", "fix: second"], (await _service.GetReleaseCommitsAsync(second, "v1.0.0")).Select(c => c.Subject));
        Assert.Equal(["feat: first"], (await _service.GetReleaseCommitsAsync(first, null)).Select(c => c.Subject));
    }

    [Fact]
    public async Task IsTagOnRemote_OnlyAfterPush()
    {
        var server = Directory.CreateTempSubdirectory("easegit-remote-").FullName;
        try
        {
            await _repo.Git.RunAsync(server, ["init", "--bare"]);
            await _repo.RunAsync("remote", "add", "origin", server);
            _repo.Write("a.txt", "1");
            await _repo.CommitAsync("first");
            await _repo.RunAsync("tag", "v1");
            var tag = (await _service.GetTagsAsync()).Single();

            Assert.False(await _service.IsTagOnRemoteAsync("origin", tag));
            Assert.True((await _service.PushTagAsync("origin", tag)).Success);
            Assert.True(await _service.IsTagOnRemoteAsync("origin", tag));
        }
        finally
        {
            Directory.Delete(server, recursive: true);
        }
    }
}
