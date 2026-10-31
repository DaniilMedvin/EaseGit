using LazyGit.Git;

namespace LazyGit.Tests.Git;

public sealed class RepoWatcherTests : IAsyncLifetime
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly TempRepository _repo = new();

    public Task InitializeAsync() => _repo.InitializeAsync();

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Theory]
    [InlineData("Program.cs", true)]
    [InlineData("src/папка/файл.cs", true)]
    [InlineData(".gitignore", true)]
    [InlineData(".git/index", true)]
    [InlineData(".git/HEAD", true)]
    [InlineData(".git/refs/heads/main", true)]
    [InlineData(".git/packed-refs", true)]
    [InlineData(".git/MERGE_HEAD", true)]
    [InlineData(".git/index.lock", false)]
    [InlineData(".git/objects/ab/cdef", false)]
    [InlineData(".git/logs/HEAD", false)]
    [InlineData(".git/refs/heads/main.lock", false)]
    public void IsRelevant_FiltersGitInternals(string path, bool expected)
    {
        Assert.Equal(expected, RepoWatcher.IsRelevant(path));
    }

    [Fact]
    public async Task Changed_FileCreated_RaisedOnceForBurst()
    {
        using var watcher = new RepoWatcher(_repo.Path);
        var count = 0;
        var raised = new TaskCompletionSource();
        watcher.Changed += (_, _) =>
        {
            Interlocked.Increment(ref count);
            raised.TrySetResult();
        };

        for (var i = 0; i < 20; i++)
            _repo.Write($"file{i}.txt", "x");

        await raised.Task.WaitAsync(Timeout);
        await Task.Delay(RepoWatcher.Debounce * 2);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Changed_GitAdd_IsRaised()
    {
        _repo.Write("a.txt", "x");
        using var watcher = new RepoWatcher(_repo.Path);
        var raised = new TaskCompletionSource();
        watcher.Changed += (_, _) => raised.TrySetResult();

        await _repo.RunAsync("add", "a.txt");

        await raised.Task.WaitAsync(Timeout);
    }
}
