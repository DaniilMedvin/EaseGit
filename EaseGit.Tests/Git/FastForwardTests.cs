using EaseGit.Git;

namespace EaseGit.Tests.Git;

/// <summary>После merge PR на GitHub: локальная база догоняет сервер только fast-forward.</summary>
public sealed class FastForwardTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private readonly TempRepository _other = new();
    private readonly string _server = Directory.CreateTempSubdirectory("easegit-server-").FullName;
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        await _other.InitializeAsync();
        await _repo.Git.RunAsync(_server, ["init", "--bare", "-b", "main"]);
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("first");
        await _repo.RunAsync("remote", "add", "origin", _server);
        await _repo.RunAsync("push", "-u", "origin", "main");
        // Второй клон — как merge на GitHub: двигает main на сервере.
        await _other.RunAsync("remote", "add", "origin", _server);
        await _other.RunAsync("pull", "origin", "main");
    }

    public async Task DisposeAsync()
    {
        await _repo.DisposeAsync();
        await _other.DisposeAsync();
        Directory.Delete(_server, recursive: true);
    }

    private async Task AdvanceServerAsync(string content)
    {
        _other.Write("b.txt", content);
        await _other.CommitAsync("merged PR " + content);
        await _other.RunAsync("push", "origin", "HEAD:main");
        Assert.True((await _service.FetchAsync()).Success);
    }

    private async Task<string> Head(string branch) => (await _repo.RunAsync("rev-parse", branch)).StdOut.Trim();

    [Fact]
    public async Task FastForwardBranch_NotCheckedOut_CatchesUpWithoutCheckout()
    {
        await _repo.RunAsync("switch", "-c", "feature");
        await AdvanceServerAsync("1");

        var result = await _service.FastForwardBranchAsync("origin", "main");

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(await Head("origin/main"), await Head("main"));
        Assert.Equal("feature", (await _service.GetStatusAsync()).Branch);
    }

    [Fact]
    public async Task FastForwardBranch_Diverged_IsRefusedAndBranchUntouched()
    {
        await _repo.RunAsync("switch", "-c", "feature");
        await _repo.RunAsync("switch", "main");
        _repo.Write("local.txt", "x");
        await _repo.CommitAsync("local only");
        var before = await Head("main");
        await _repo.RunAsync("switch", "feature");
        await AdvanceServerAsync("2");

        var result = await _service.FastForwardBranchAsync("origin", "main");

        Assert.False(result.Success);
        Assert.Equal(before, await Head("main"));
    }

    [Fact]
    public async Task FastForwardCurrent_OnBase_CatchesUp()
    {
        await AdvanceServerAsync("3");

        var result = await _service.FastForwardCurrentAsync();

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(await Head("origin/main"), await Head("HEAD"));
    }

    [Fact]
    public async Task BackgroundFetch_GetsNewCommitsWithoutLogging()
    {
        var log = new CommandLog();
        var service = new GitService(_repo.Git, _repo.Path, log);
        _other.Write("c.txt", "4");
        await _other.CommitAsync("merged on GitHub");
        await _other.RunAsync("push", "origin", "HEAD:main");

        var result = await service.BackgroundFetchAsync();

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(1, (await service.GetStatusAsync()).Behind);
        Assert.Empty(log.Entries);
    }

    [Theory]
    [InlineData("ssh", "ssh -o BatchMode=yes")]
    [InlineData("ssh -i ~/.ssh/work", "ssh -i ~/.ssh/work -o BatchMode=yes")]
    [InlineData("ssh -o BatchMode=no", "ssh -o BatchMode=no")]
    public void BatchSsh_AddsBatchModeOnce(string ssh, string expected) => Assert.Equal(expected, GitService.BatchSsh(ssh));
}
