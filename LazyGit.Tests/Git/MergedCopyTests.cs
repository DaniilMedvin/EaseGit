using LazyGit.Git;
using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git;

public sealed class MergedCopyTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("base.txt", "base\n");
        await _repo.CommitAsync("base");
        await _repo.RunAsync("switch", "-c", "feature");
        _repo.Write("a.txt", "a\n");
        await _repo.CommitAsync("feat: a");
        _repo.Write("b.txt", "b\n");
        await _repo.CommitAsync("feat: b");
        await _repo.RunAsync("switch", "main");
        _repo.Write("main.txt", "main moved on\n");
        await _repo.CommitAsync("main work");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private async Task<string> TipAsync(string branch) => (await _service.GetBranchTipsAsync())[branch];

    private async Task<string> HeadAsync() => (await _repo.RunAsync("rev-parse", "HEAD")).StdOut.Trim();

    [Fact]
    public async Task RebaseMerged_FoundWithCopyOfTip()
    {
        await _repo.RunAsync("cherry-pick", "main..feature");

        var found = await _service.FindMergedCopyAsync("feature", await TipAsync("feature"), "main");

        Assert.NotNull(found);
        Assert.False(found.Squashed);
        Assert.Equal(await HeadAsync(), found.Commit);
    }

    [Fact]
    public async Task SquashMerged_FoundAsOneCommit()
    {
        await _repo.RunAsync("merge", "--squash", "feature");
        await _repo.RunAsync("commit", "-m", "feat: a and b (squash)");

        var found = await _service.FindMergedCopyAsync("feature", await TipAsync("feature"), "main");

        Assert.True(found?.Squashed);
        Assert.Equal(await HeadAsync(), found!.Commit);
    }

    [Fact]
    public async Task NormalMerge_NotAMergedCopy()
    {
        await _repo.RunAsync("merge", "--no-edit", "feature");

        Assert.Null(await _service.FindMergedCopyAsync("feature", await TipAsync("feature"), "main"));
    }

    [Fact]
    public async Task NotMergedOrPartially_Null()
    {
        Assert.Null(await _service.FindMergedCopyAsync("feature", await TipAsync("feature"), "main"));

        await _repo.RunAsync("cherry-pick", "feature~1");
        Assert.Null(await _service.FindMergedCopyAsync("feature", await TipAsync("feature"), "main"));
    }

    [Fact]
    public void PatchIdParser_PairsAndZeroCommit()
    {
        var pairs = PatchIdParser.Parse("f1 c1\nf2 0000000000000000000000000000000000000000\n");

        Assert.Equal([("f1", "c1"), ("f2", "0000000000000000000000000000000000000000")], pairs);
    }
}
