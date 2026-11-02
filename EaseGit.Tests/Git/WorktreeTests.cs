using EaseGit.Git;
using EaseGit.Git.Parsers;

namespace EaseGit.Tests.Git;

public sealed class WorktreeTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;
    private readonly List<string> _folders = [];

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.txt", "a\n");
        await _repo.CommitAsync("init");
    }

    public async Task DisposeAsync()
    {
        await _repo.DisposeAsync();
        foreach (var folder in _folders.Where(Directory.Exists))
            Directory.Delete(folder, recursive: true);
    }

    private string Folder(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"easegit-wt-{Guid.NewGuid():N}-{name}");
        _folders.Add(path);
        return path;
    }

    [Fact]
    public void Parse_Sample_WithLockReasonInCyrillic()
    {
        var worktrees = WorktreeParser.Parse(
            "worktree /p/main\0HEAD aaa\0branch refs/heads/main\0\0" +
            "worktree /p/wt\0HEAD bbb\0detached\0locked на флешке\0\0" +
            "worktree /p/gone\0HEAD ccc\0branch refs/heads/feature/x\0prunable gitdir file points to non-existent location\0\0");

        Assert.Equal(3, worktrees.Count);
        Assert.True(worktrees[0].IsMain);
        Assert.Equal("main", worktrees[0].Branch);
        Assert.Null(worktrees[1].Branch);
        Assert.Equal("на флешке", worktrees[1].Locked);
        Assert.Equal("feature/x", worktrees[2].Branch);
        Assert.NotNull(worktrees[2].Prunable);
    }

    [Fact]
    public async Task AddListCountRemove()
    {
        var path = Folder("feature");

        Assert.True((await _service.AddWorktreeAsync(path, "feature/new", newBranch: true)).Success);
        var worktrees = await _service.GetWorktreesAsync();
        Assert.Equal(2, worktrees.Count);
        Assert.Equal("feature/new", worktrees[1].Branch);
        Assert.Equal(0, await _service.CountChangesAsync(path));

        File.WriteAllText(Path.Combine(path, "dirty.txt"), "x");
        Assert.Equal(1, await _service.CountChangesAsync(path));
        Assert.False((await _service.RemoveWorktreeAsync(path, force: false)).Success);
        Assert.True((await _service.RemoveWorktreeAsync(path, force: true)).Success);
        Assert.Single(await _service.GetWorktreesAsync());
    }

    [Fact]
    public async Task Prune_ManuallyDeletedFolder()
    {
        var path = Folder("gone");
        await _service.AddWorktreeAsync(path, "gone", newBranch: true);
        Directory.Delete(path, recursive: true);

        Assert.NotNull((await _service.GetWorktreesAsync())[1].Prunable);
        Assert.True((await _service.PruneWorktreesAsync()).Success);
        Assert.Single(await _service.GetWorktreesAsync());
    }
}
