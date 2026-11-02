using EaseGit.Git;

namespace EaseGit.Tests.Git;

public sealed class CommandLogTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private readonly CommandLog _log = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path, _log);
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task Actions_AreLogged_ReadsAreNot()
    {
        _repo.Write("a.txt", "1");
        var file = (await _service.GetStatusAsync()).Files.Single();

        await _service.StageAsync(file);
        await _service.CommitAsync("init");
        await _service.GetLogAsync();
        await _service.GetBranchesAsync();

        Assert.Equal(
            [["add", "--", "a.txt"], ["commit", "--file=-", "--cleanup=strip"]],
            _log.Entries.Select(e => e.Args.ToArray()));
        Assert.All(_log.Entries, e => Assert.True(e.Success));
    }

    [Fact]
    public async Task FailedAction_LoggedWithOutput()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");

        await _service.CreateBranchAsync("main");

        var entry = Assert.Single(_log.Entries);
        Assert.False(entry.Success);
        Assert.Contains("already exists", entry.Output);
    }

    [Fact]
    public void Add_OverCapacity_DropsOldest()
    {
        for (var i = 0; i < CommandLog.Capacity + 5; i++)
            _log.Add(new CommandLogEntry(DateTimeOffset.Now, [$"cmd{i}"], 0, TimeSpan.Zero, ""));

        Assert.Equal(CommandLog.Capacity, _log.Entries.Count);
        Assert.Equal("cmd5", _log.Entries[0].Args[0]);
    }

    [Fact]
    public async Task CancelledAction_LoggedAsCancelled()
    {
        await _repo.RunAsync("config", "protocol.ext.allow", "always");
        await _repo.RunAsync("remote", "add", "origin", "ext::sh -c sleep% 30");
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _service.FetchAsync(cts.Token));

        Assert.True(Assert.Single(_log.Entries).Cancelled);
    }
}
