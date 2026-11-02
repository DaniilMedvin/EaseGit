using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

/// <summary>Push, pull и fetch через локальный bare-репозиторий вместо сервера.</summary>
public sealed class SyncTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private readonly TempRepository _other = new();
    private readonly string _remote = Directory.CreateTempSubdirectory("easegit-remote-").FullName;
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        await _other.InitializeAsync();
        await _repo.RunAsync("init", "--bare", "-b", "main", _remote);
        await _repo.RunAsync("remote", "add", "origin", _remote);
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        _service = new GitService(_repo.Git, _repo.Path);
    }

    public async Task DisposeAsync()
    {
        await _repo.DisposeAsync();
        await _other.DisposeAsync();
        Directory.Delete(_remote, recursive: true);
    }

    /// <summary>Второй клон отправляет на «сервер» новый коммит.</summary>
    private async Task PushFromOtherAsync(string file)
    {
        await _other.RunAsync("remote", "add", "origin", _remote);
        await _other.RunAsync("pull", "origin", "main");
        _other.Write(file, "x");
        await _other.CommitAsync("from other");
        await _other.RunAsync("push", "origin", "main");
    }

    [Fact]
    public async Task PushSetUpstreamAsync_NewBranch_PublishesAndTracks()
    {
        Assert.Equal(["origin"], await _service.GetRemotesAsync());

        var result = await _service.PushSetUpstreamAsync("origin", "main");

        Assert.True(result.Success, result.StdErr);
        var status = await _service.GetStatusAsync();
        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal(0, status.Ahead);
    }

    [Fact]
    public async Task FetchAndPull_NewCommitOnServer_BehindThenUpToDate()
    {
        await _service.PushSetUpstreamAsync("origin", "main");
        await PushFromOtherAsync("b.txt");

        var fetch = await _service.FetchAsync();
        var behind = (await _service.GetStatusAsync()).Behind;
        var pull = await _service.PullAsync();

        Assert.True(fetch.Success, fetch.StdErr);
        Assert.Equal(1, behind);
        Assert.True(pull.Success, pull.StdErr);
        Assert.Contains("Fast-forward", pull.StdOut);
        Assert.Equal(0, (await _service.GetStatusAsync()).Behind);
    }

    [Fact]
    public async Task PushAsync_ServerHasNewCommits_IsRejected()
    {
        await _service.PushSetUpstreamAsync("origin", "main");
        await PushFromOtherAsync("b.txt");
        _repo.Write("c.txt", "local");
        await _repo.CommitAsync("local");

        var result = await _service.PushAsync();

        Assert.False(result.Success);
        Assert.Contains("Push отклонён", EaseGit.UI.GitErrors.Explain(result));
    }

    [Fact]
    public async Task RunAsync_Cancelled_KillsGitQuickly()
    {
        // ext:: позволяет подставить вместо сервера команду — здесь «сервер», который молчит 30 секунд.
        await _repo.RunAsync("config", "protocol.ext.allow", "always");
        await _repo.RunAsync("remote", "add", "slow", "ext::sh -c sleep% 30");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var started = DateTime.UtcNow;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _repo.Git.RunAsync(_repo.Path, ["fetch", "slow"], cts.Token));

        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }
}
