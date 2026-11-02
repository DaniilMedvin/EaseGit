using EaseGit.Git;
using EaseGit.Git.Parsers;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

public sealed class SmallActionsTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.txt", "1\n");
        await _repo.CommitAsync("first");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task RenameBranch_Current_KeepsCheckout()
    {
        var main = (await _service.GetBranchesAsync()).Single();

        var result = await _service.RenameBranchAsync(main, "trunk");

        Assert.True(result.Success, result.StdErr);
        Assert.Equal("trunk", (await _service.GetStatusAsync()).Branch);
    }

    [Fact]
    public async Task Tags_CreateListAndDelete()
    {
        Assert.True((await _service.CreateTagOnHeadAsync("v1", "")).Success);
        Assert.True((await _service.CreateTagOnHeadAsync("v2-rc", "Кандидат в релиз")).Success);

        var tags = await _service.GetTagsAsync();
        var head = (await _service.GetLogAsync())[0].Hash;
        Assert.Equal(["v1", "v2-rc"], tags.Select(t => t.Name).Order());
        Assert.All(tags, t => Assert.Equal(head, t.Hash));
        Assert.True(tags.Single(t => t.Name == "v2-rc").Annotated);
        Assert.Equal("Кандидат в релиз", tags.Single(t => t.Name == "v2-rc").Subject);
        Assert.Equal("first", tags.Single(t => t.Name == "v1").Subject);

        Assert.True((await _service.DeleteTagAsync(tags[0], remote: null)).Success);
        Assert.Single(await _service.GetTagsAsync());
    }

    [Fact]
    public void TagArguments_PushAndDeleteRemoteUseFullRef()
    {
        var tag = new Tag("v1", "abc", false, default, "s");

        Assert.Equal(["push", "origin", "refs/tags/v1"], GitService.PushTagArguments("origin", tag));
        Assert.Equal(["push", "origin", "--delete", "refs/tags/v1"], GitService.DeleteRemoteTagArguments("origin", tag));
    }

    [Fact]
    public async Task StashPaths_OnlySelectedFilesIncludingNew()
    {
        _repo.Write("a.txt", "2\n");
        _repo.Write("b.txt", "new\n");
        _repo.Write("c.txt", "other new\n");

        var result = await _service.StashPathsAsync("только a и b", ["a.txt", "b.txt"]);

        Assert.True(result.Success, result.StdErr);
        var left = (await _service.GetStatusAsync()).Files.Select(f => f.Path).ToList();
        Assert.Equal(["c.txt"], left);
        Assert.Equal("только a и b", (await _service.GetStashesAsync())[0].Message);
    }

    [Fact]
    public async Task Skip_CherryPickConflict_DropsCommit()
    {
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("a.txt", "theirs\n");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "ours\n");
        await _repo.CommitAsync("ours");
        await _repo.Git.RunAsync(_repo.Path, ["cherry-pick", "other"]);
        Assert.Equal(RepoOperation.CherryPick, await _service.GetOperationAsync());

        var result = await _service.SkipAsync(RepoOperation.CherryPick);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
        Assert.Equal("ours", (await _service.GetLogAsync())[0].Subject);
    }

    [Fact]
    public void TagParser_LightweightAndAnnotated()
    {
        var tags = TagParser.Parse(
            "v0.2.0-rc\u001ftag\u001fc0ffee\u001fdeadbeef\u001f2026-11-01T23:47:34+02:00\u001fRelease candidate\0" +
            "\nv0.1.0\u001fcommit\u001f\u001fe0934a1\u001f2026-10-28T21:51:44+02:00\u001ffix: handle\0");

        Assert.Equal(("c0ffee", true), (tags[0].Hash, tags[0].Annotated));
        Assert.Equal(("e0934a1", false), (tags[1].Hash, tags[1].Annotated));
        Assert.Equal(28, tags[1].Date.Day);
    }
}
