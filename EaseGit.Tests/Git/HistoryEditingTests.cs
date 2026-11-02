using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

public sealed class HistoryEditingTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.txt", "1\n");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "2\n");
        await _repo.CommitAsync("second");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private async Task<Commit> CommitAtAsync(int index) => (await _service.GetLogAsync())[index];

    [Fact]
    public async Task Amend_StagedChange_JoinsLastCommitKeepingMessage()
    {
        var before = (await CommitAtAsync(0)).Hash;
        _repo.Write("b.txt", "new\n");
        await _repo.RunAsync("add", "b.txt");

        var result = await _service.AmendAsync();

        var log = await _service.GetLogAsync();
        Assert.True(result.Success, result.StdErr);
        Assert.Equal(2, log.Count);
        Assert.Equal("second", log[0].Subject);
        Assert.NotEqual(before, log[0].Hash);
        Assert.Contains("b.txt", (await _repo.RunAsync("show", "--name-only", "--format=", "HEAD")).StdOut);
    }

    [Theory]
    [InlineData(ResetMode.Soft, 1, "staged")]
    [InlineData(ResetMode.Mixed, 1, "modified")]
    [InlineData(ResetMode.Hard, 1, "clean")]
    public async Task Reset_ToFirstCommit_KeepsChangesAccordingToMode(ResetMode mode, int expectedCommits, string expected)
    {
        var first = await CommitAtAsync(1);

        var result = await _service.ResetAsync(first, mode);

        var status = await _service.GetStatusAsync();
        Assert.True(result.Success, result.StdErr);
        Assert.Equal(expectedCommits, (await _service.GetLogAsync()).Count);
        var file = status.Files.FirstOrDefault(f => f.Path == "a.txt");
        switch (expected)
        {
            case "staged":
                Assert.True(file?.HasStagedChanges);
                break;
            case "modified":
                Assert.False(file?.HasStagedChanges);
                Assert.True(file?.HasUnstagedChanges);
                Assert.False(file?.IsUntracked);
                break;
            default:
                Assert.Null(file);
                break;
        }

        Assert.Equal(mode == ResetMode.Hard ? "1\n" : "2\n", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
    }

    [Fact]
    public async Task Revert_LastCommit_AddsCommitUndoingIt()
    {
        var second = await CommitAtAsync(0);

        var result = await _service.RevertAsync(second);

        var log = await _service.GetLogAsync();
        Assert.True(result.Success, result.StdErr);
        Assert.Equal(3, log.Count);
        Assert.StartsWith("Revert \"second\"", log[0].Subject);
        Assert.Equal("1\n", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
    }

    [Fact]
    public async Task Revert_ConflictingCommit_LeavesRevertInProgress()
    {
        var second = await CommitAtAsync(0);
        _repo.Write("a.txt", "3\n");
        await _repo.CommitAsync("third");

        var result = await _service.RevertAsync(second);

        Assert.False(result.Success);
        Assert.Equal(RepoOperation.Revert, await _service.GetOperationAsync());
    }

    [Fact]
    public async Task CherryPick_CommitFromOtherBranch_CopiesIt()
    {
        await _repo.RunAsync("switch", "-c", "feature");
        _repo.Write("f.txt", "feature\n");
        await _repo.CommitAsync("feat: на другой ветке");
        var picked = await CommitAtAsync(0);
        await _repo.RunAsync("switch", "main");

        var result = await _service.CherryPickAsync(picked.Hash);

        var log = await _service.GetLogAsync();
        Assert.True(result.Success, result.StdErr);
        Assert.Equal("feat: на другой ветке", log[0].Subject);
        Assert.True(File.Exists(Path.Combine(_repo.Path, "f.txt")));
    }

    [Fact]
    public async Task CherryPick_Conflict_LeavesCherryPickInProgress()
    {
        await _repo.RunAsync("switch", "-c", "feature", "HEAD~1");
        _repo.Write("a.txt", "other\n");
        await _repo.CommitAsync("feat: other");
        var picked = await CommitAtAsync(0);
        await _repo.RunAsync("switch", "main");

        var result = await _service.CherryPickAsync(picked.Hash);

        Assert.False(result.Success);
        Assert.Equal(RepoOperation.CherryPick, await _service.GetOperationAsync());
        Assert.Contains((await _service.GetStatusAsync()).Files, f => f.IsConflicted);
    }

    [Fact]
    public async Task FindCommit_BranchOrShortHash_ReturnsCommit()
    {
        var head = await CommitAtAsync(0);

        var byBranch = await _service.FindCommitAsync("main");
        var byShort = await _service.FindCommitAsync(head.ShortHash);

        Assert.Equal(head.Hash, byBranch?.Hash);
        Assert.Equal(head.Hash, byShort?.Hash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--all")]
    [InlineData("-h")]
    [InlineData("no-such-branch")]
    public async Task FindCommit_UnknownOrOptionLike_ReturnsNull(string revision)
    {
        Assert.Null(await _service.FindCommitAsync(revision));
    }

    [Fact]
    public void ResetArguments_Mode_BecomesFlag()
    {
        var commit = new Commit("a1b2c3d4e5f6", "A", "a@e", DateTimeOffset.Now, "x");

        Assert.Equal(["reset", "--hard", "a1b2c3d4e5f6"], GitService.ResetArguments(commit, ResetMode.Hard));
    }
}
