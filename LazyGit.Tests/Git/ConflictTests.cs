using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

public sealed class ConflictTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private string Read(string file) => File.ReadAllText(Path.Combine(_repo.Path, file));

    /// <summary>main и other меняют a.txt по-разному; other вливается в main с конфликтом.</summary>
    private async Task MergeWithConflictAsync()
    {
        _repo.Write("a.txt", "base");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("a.txt", "theirs");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "ours");
        await _repo.CommitAsync("ours");
        await _repo.Git.RunAsync(_repo.Path, ["merge", "other"]);
    }

    [Fact]
    public async Task GetOperationAsync_CleanRepository_None()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");

        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
    }

    [Fact]
    public async Task GetOperationAsync_MergeConflict_Merge()
    {
        await MergeWithConflictAsync();

        Assert.Equal(RepoOperation.Merge, await _service.GetOperationAsync());
    }

    [Fact]
    public async Task GetOperationAsync_CherryPickConflict_CherryPick()
    {
        await MergeWithConflictAsync();
        await _repo.RunAsync("merge", "--abort");
        await _repo.Git.RunAsync(_repo.Path, ["cherry-pick", "other"]);

        Assert.Equal(RepoOperation.CherryPick, await _service.GetOperationAsync());
    }

    [Theory]
    [InlineData(true, "ours")]
    [InlineData(false, "theirs")]
    public async Task ResolveConflictAsync_KeepsChosenSideAndStages(bool ours, string expected)
    {
        await MergeWithConflictAsync();
        var conflicted = (await _service.GetStatusAsync()).Files.Single();

        var result = await _service.ResolveConflictAsync([conflicted], ours);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(expected, Read("a.txt"));
        Assert.DoesNotContain((await _service.GetStatusAsync()).Files, f => f.IsConflicted);
    }

    [Fact]
    public async Task ResolveBothAsync_KeepsOursThenTheirs_NoTempFilesLeft()
    {
        _repo.Write("a.txt", "one\ntwo\nthree\n");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("a.txt", "one\nTHEIRS\nthree\n");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "one\nOURS\nthree\n");
        await _repo.CommitAsync("ours");
        await _repo.Git.RunAsync(_repo.Path, ["merge", "other"]);

        var result = await _service.ResolveBothAsync([(await _service.GetStatusAsync()).Files.Single()]);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal("one\nOURS\nTHEIRS\nthree\n", Read("a.txt"));
        Assert.DoesNotContain((await _service.GetStatusAsync()).Files, f => f.IsConflicted);
        Assert.Empty(Directory.GetFiles(_repo.Path, ".merge_file_*"));
    }

    [Fact]
    public async Task ResolveBothAsync_AddedInBothBranches_UsesEmptyBase()
    {
        _repo.Write("base.txt", "x");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("new.txt", "theirs\n");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("new.txt", "ours\n");
        await _repo.CommitAsync("ours");
        await _repo.Git.RunAsync(_repo.Path, ["merge", "other"]);

        var result = await _service.ResolveBothAsync([(await _service.GetStatusAsync()).Files.Single()]);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal("ours\ntheirs\n", Read("new.txt"));
        Assert.Empty(Directory.GetFiles(_repo.Path, ".merge_file_*"));
    }

    [Fact]
    public async Task AbortAsync_Merge_BackToBeforeMerge()
    {
        await MergeWithConflictAsync();

        var result = await _service.AbortAsync(RepoOperation.Merge);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
        Assert.Equal("ours", Read("a.txt"));
        Assert.Empty((await _service.GetStatusAsync()).Files);
    }
}
