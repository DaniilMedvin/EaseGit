using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

public sealed class FixupTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.cs", "class A { int x = 1; }\n");
        await _repo.CommitAsync("feat: add A");
        _repo.Write("b.cs", "class B { }\n");
        await _repo.CommitAsync("feat: add B");
        _repo.Write("c.cs", "class C { }\n");
        await _repo.CommitAsync("feat: add C");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private async Task<string> ShowAsync(string revision, string file) => (await _repo.RunAsync("show", $"{revision}:{file}")).StdOut;

    [Fact]
    public async Task Fixup_StagedChange_GoesIntoOldCommitKeepingHistory()
    {
        var log = await _service.GetLogAsync();
        _repo.Write("a.cs", "class A { int x = 2; }\n");
        await _repo.RunAsync("add", "a.cs");
        _repo.Write("c.cs", "class C { /* unstaged */ }\n");

        var result = await _service.FixupAsync(log[2]);

        Assert.True(result.Success, result.StdErr);
        var after = await _service.GetLogAsync();
        Assert.Equal(["feat: add C", "feat: add B", "feat: add A"], after.Select(c => c.Subject));
        Assert.Contains("x = 2", await ShowAsync(after[2].Hash, "a.cs"));
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
        // Незакоммиченное вне индекса пережило rebase (--autostash).
        Assert.Contains("unstaged", File.ReadAllText(Path.Combine(_repo.Path, "c.cs")));
    }

    [Fact]
    public async Task Fixup_RootCommit_UsesRoot()
    {
        var root = (await _service.GetLogAsync())[^1];
        _repo.Write("a.cs", "class A { int x = 3; }\n");
        await _repo.RunAsync("add", "a.cs");

        var result = await _service.FixupAsync(root);

        Assert.True(result.Success, result.StdErr);
        var after = await _service.GetLogAsync();
        Assert.Equal(3, after.Count);
        Assert.Contains("x = 3", await ShowAsync(after[^1].Hash, "a.cs"));
        Assert.Contains("--root", GitService.AutosquashArguments(root));
    }

    [Fact]
    public async Task Fixup_NothingStaged_FailsWithoutRebase()
    {
        var before = (await _service.GetLogAsync())[0].Hash;

        var result = await _service.FixupAsync((await _service.GetLogAsync())[1]);

        Assert.False(result.Success);
        Assert.Equal(before, (await _service.GetLogAsync())[0].Hash);
    }
}
