using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

/// <summary>Действия окна графа на настоящем репозитории: ветка, тег, checkout удалённой ветки, история всех веток.</summary>
public sealed class GraphActionsTests : IAsyncLifetime
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

    private async Task<Commit> CommitAsync(string subject) =>
        (await _service.GetLogAsync(allBranches: true)).Single(c => c.Subject == subject);

    [Fact]
    public async Task CreateBranchAt_OldCommit_SwitchesToNewBranchThere()
    {
        var first = await CommitAsync("first");

        var result = await _service.CreateBranchAtAsync("hotfix/old", first);

        Assert.True(result.Success, result.StdErr);
        var status = await _service.GetStatusAsync();
        Assert.Equal("hotfix/old", status.Branch);
        Assert.Equal(first.Hash, status.HeadHash);
    }

    [Fact]
    public async Task CreateTag_ShowsAsRefOnCommit()
    {
        var first = await CommitAsync("first");

        await _service.CreateTagAsync("v0.1", first);

        Assert.Contains(new CommitRef("v0.1", RefKind.Tag), (await CommitAsync("first")).Refs);
    }

    [Fact]
    public async Task GetLog_AllBranches_IncludesOtherBranchButNotStash()
    {
        await _repo.RunAsync("switch", "-c", "feature");
        _repo.Write("b.txt", "f\n");
        await _repo.CommitAsync("feature work");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "stash me\n");
        await _repo.RunAsync("stash");

        var current = await _service.GetLogAsync();
        var all = await _service.GetLogAsync(allBranches: true);

        Assert.DoesNotContain(current, c => c.Subject == "feature work");
        Assert.Contains(all, c => c.Subject == "feature work");
        Assert.DoesNotContain(all, c => c.Subject.Contains("WIP on"));
    }

    [Fact]
    public async Task Checkout_RemoteBranch_CreatesTrackingLocalBranch()
    {
        var remote = Path.Combine(_repo.Path, "..", Path.GetFileName(_repo.Path) + "-remote.git");
        try
        {
            await _repo.RunAsync("init", "--bare", "-b", "main", remote);
            await _repo.RunAsync("remote", "add", "origin", remote);
            await _repo.RunAsync("switch", "-c", "feature");
            await _repo.RunAsync("push", "origin", "feature");
            await _repo.RunAsync("switch", "main");
            await _repo.RunAsync("branch", "-D", "feature");
            var commit = await CommitAsync("second");

            var result = await _service.CheckoutAsync(new CommitRef("origin/feature", RefKind.Remote), commit);

            Assert.True(result.Success, result.StdErr);
            var branch = (await _service.GetBranchesAsync()).Single(b => b.IsCurrent);
            Assert.Equal("feature", branch.Name);
            Assert.Equal("origin/feature", branch.Upstream);
        }
        finally
        {
            Directory.Delete(remote, recursive: true);
        }
    }

    [Fact]
    public void CheckoutArguments_NoRef_DetachesAtCommit()
    {
        var commit = new Commit("abc123", "a", "a@a", DateTimeOffset.UnixEpoch, "s");

        Assert.Equal(["switch", "--detach", "abc123"], GitService.CheckoutArguments(null, commit));
        Assert.Equal(["switch", "--no-guess", "main"], GitService.CheckoutArguments(new CommitRef("main", RefKind.Branch), commit));
    }

    [Fact]
    public async Task GetBranchGraphLog_OtherBranch_ReturnsItsCommitsWithoutCheckout()
    {
        await _repo.RunAsync("switch", "-c", "feature/log");
        _repo.Write("feature.txt", "f\n");
        await _repo.CommitAsync("feat: only in feature");
        await _repo.RunAsync("switch", "main");

        var branch = (await _service.GetBranchesAsync()).Single(b => b.Name == "feature/log");
        var log = await _service.GetBranchGraphLogAsync(branch);

        Assert.Equal("feat: only in feature", log.First(l => l.Kind == GraphLogLineKind.Commit).Text);
        Assert.Equal("main", (await _service.GetStatusAsync()).Branch);
    }

    [Fact]
    public async Task GetCommitSubjects_Range_OldestFirstAndUnknownRangeEmpty()
    {
        await _repo.RunAsync("switch", "-c", "feature/subjects");
        _repo.Write("s1.txt", "1\n");
        await _repo.CommitAsync("feat: one");
        _repo.Write("s2.txt", "2\n");
        await _repo.CommitAsync("feat: two");

        Assert.Equal(["feat: one", "feat: two"], await _service.GetCommitSubjectsAsync("main..HEAD"));
        Assert.Empty(await _service.GetCommitSubjectsAsync("origin/nothing..HEAD"));
    }

    [Fact]
    public async Task GetRemoteDefaultBranch_FromOriginHead()
    {
        Assert.Null(await _service.GetRemoteDefaultBranchAsync());
        await _repo.RunAsync("update-ref", "refs/remotes/origin/main", "HEAD");
        await _repo.RunAsync("symbolic-ref", "refs/remotes/origin/HEAD", "refs/remotes/origin/main");

        Assert.Equal("main", await _service.GetRemoteDefaultBranchAsync());
    }
}
