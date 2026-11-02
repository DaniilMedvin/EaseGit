using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

public sealed class GitServiceTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task OpenAsync_Subdirectory_ReturnsRepoRoot()
    {
        Directory.CreateDirectory(Path.Combine(_repo.Path, "src", "deep"));

        var service = await GitService.OpenAsync(_repo.Git, Path.Combine(_repo.Path, "src", "deep"));

        Assert.NotNull(service);
        Assert.Equal(_repo.Path, service.RepoRoot);
    }

    [Fact]
    public async Task OpenAsync_NotARepository_ReturnsNull()
    {
        var dir = Directory.CreateTempSubdirectory("easegit-not-repo-").FullName;
        try
        {
            Assert.Null(await GitService.OpenAsync(_repo.Git, dir));
        }
        finally
        {
            Directory.Delete(dir);
        }
    }

    [Fact]
    public async Task InitAsync_MissingDirectory_CreatesRepository()
    {
        var dir = Path.Combine(Directory.CreateTempSubdirectory("easegit-init-").FullName, "новый репозиторий");
        try
        {
            var result = await GitService.InitAsync(_repo.Git, dir);

            Assert.True(result.Success, result.StdErr);
            Assert.True(Directory.Exists(Path.Combine(dir, ".git")));
            Assert.NotNull(await GitService.OpenAsync(_repo.Git, dir));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(dir)!, recursive: true);
        }
    }

    [Fact]
    public async Task EmptyRepository_StatusAndLogWork()
    {
        var status = await _service.GetStatusAsync();
        var log = await _service.GetLogAsync();

        Assert.Equal("main", status.Branch);
        Assert.Null(status.HeadHash);
        Assert.Empty(log);
        Assert.False(await _service.HasCommitsAsync());
    }

    [Fact]
    public async Task GetLogAsync_RespectsLimit()
    {
        for (var i = 1; i <= 3; i++)
        {
            _repo.Write("f.txt", i.ToString());
            await _repo.CommitAsync($"commit {i}");
        }

        var log = await _service.GetLogAsync(limit: 2);

        Assert.Equal(["commit 3", "commit 2"], log.Select(c => c.Subject));
    }

    [Fact]
    public async Task GetBranchesAsync_ReturnsCurrentBranch()
    {
        _repo.Write("f.txt", "1");
        await _repo.CommitAsync("init");

        var branch = Assert.Single(await _service.GetBranchesAsync());

        Assert.Equal("main", branch.Name);
        Assert.True(branch.IsCurrent);
    }

    [Fact]
    public async Task StageAsync_UntrackedFile_BecomesAdded()
    {
        _repo.Write("new файл.txt", "x");

        var result = await _service.StageAsync(await SingleFileAsync());

        Assert.True(result.Success, result.StdErr);
        var file = await SingleFileAsync();
        Assert.Equal(ChangeKind.Added, file.Staged);
        Assert.Equal(ChangeKind.Unmodified, file.Unstaged);
    }

    [Fact]
    public async Task UnstageAsync_EmptyRepository_BecomesUntracked()
    {
        _repo.Write("a.txt", "x");
        await _repo.RunAsync("add", "a.txt");

        var result = await _service.UnstageAsync(await SingleFileAsync());

        Assert.True(result.Success, result.StdErr);
        Assert.True((await SingleFileAsync()).IsUntracked);
    }

    [Fact]
    public async Task StageAndUnstage_ModifiedFile_MovesBetweenIndexAndWorkTree()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        _repo.Write("a.txt", "2");

        await _service.StageAsync(await SingleFileAsync());
        var staged = await SingleFileAsync();
        await _service.UnstageAsync(staged);
        var unstaged = await SingleFileAsync();

        Assert.Equal((ChangeKind.Modified, ChangeKind.Unmodified), (staged.Staged, staged.Unstaged));
        Assert.Equal((ChangeKind.Unmodified, ChangeKind.Modified), (unstaged.Staged, unstaged.Unstaged));
    }

    [Fact]
    public async Task StageAndUnstage_SeveralFiles_MovesAllAtOnce()
    {
        _repo.Write("dir/a.txt", "1");
        _repo.Write("dir/sub/b.txt", "2");
        await _repo.CommitAsync("init");
        _repo.Write("dir/a.txt", "3");
        _repo.Write("dir/sub/b.txt", "4");

        await _service.StageAsync((await _service.GetStatusAsync()).Files);
        var staged = (await _service.GetStatusAsync()).Files;
        await _service.UnstageAsync(staged);
        var unstaged = (await _service.GetStatusAsync()).Files;

        Assert.Equal(2, staged.Count);
        Assert.All(staged, f => Assert.Equal((ChangeKind.Modified, ChangeKind.Unmodified), (f.Staged, f.Unstaged)));
        Assert.All(unstaged, f => Assert.Equal((ChangeKind.Unmodified, ChangeKind.Modified), (f.Staged, f.Unstaged)));
    }

    [Fact]
    public async Task GetCommitDiffAsync_FirstAndSecondCommit_ReturnsMessageAndChanges()
    {
        _repo.Write("a.txt", "1\n");
        await _repo.CommitAsync("feat: add a");
        _repo.Write("a.txt", "2\n");
        await _repo.RunAsync("commit", "-am", "fix: change a", "-m", "- explain why");
        var log = await _service.GetLogAsync();

        var second = await _service.GetCommitDiffAsync(log[0]);
        var first = await _service.GetCommitDiffAsync(log[1]);

        Assert.Equal("fix: change a\n\n- explain why", second.Message);
        Assert.Contains(new DiffLine(DiffLineKind.Removed, "1", OldNumber: 1), second.Lines);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "2", NewNumber: 1), second.Lines);
        Assert.Equal("feat: add a", first.Message);
        Assert.Contains(first.Lines, l => l.Kind == DiffLineKind.Header && l.Text.StartsWith("new file mode"));
    }

    [Fact]
    public async Task CreateBranchAndSwitch_ChangesCurrentBranch()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");

        var created = await _service.CreateBranchAsync("feature/новая");
        var afterCreate = await _service.GetBranchesAsync();
        var switched = await _service.SwitchAsync(afterCreate.Single(b => b.Name == "main"));
        var afterSwitch = await _service.GetBranchesAsync();

        Assert.True(created.Success, created.StdErr);
        Assert.True(switched.Success, switched.StdErr);
        Assert.Equal("feature/новая", afterCreate.Single(b => b.IsCurrent).Name);
        Assert.Equal("main", afterSwitch.Single(b => b.IsCurrent).Name);
    }

    [Fact]
    public async Task SwitchAsync_ConflictingLocalChanges_FailsWithoutLosingThem()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("a.txt", "other");
        await _repo.CommitAsync("change on other");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "local");

        var other = (await _service.GetBranchesAsync()).Single(b => b.Name == "other");
        var result = await _service.SwitchAsync(other);

        Assert.False(result.Success);
        Assert.Contains("would be overwritten by checkout", result.StdErr);
        Assert.Equal("local", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
    }

    [Fact]
    public async Task DeleteBranchAsync_Unmerged_RefusesUntilForced()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "work");
        _repo.Write("b.txt", "2");
        await _repo.CommitAsync("work 1");
        _repo.Write("b.txt", "3");
        await _repo.CommitAsync("work 2");
        await _repo.RunAsync("switch", "main");
        var work = (await _service.GetBranchesAsync()).Single(b => b.Name == "work");

        var unmerged = await _service.CountUnmergedAsync(work);
        var safe = await _service.DeleteBranchAsync(work, force: false);
        var forced = await _service.DeleteBranchAsync(work, force: true);

        Assert.Equal(2, unmerged);
        Assert.False(safe.Success);
        Assert.Contains("is not fully merged", safe.StdErr);
        Assert.True(forced.Success, forced.StdErr);
        Assert.DoesNotContain(await _service.GetBranchesAsync(), b => b.Name == "work");
    }

    [Fact]
    public async Task MergeAsync_BranchAhead_FastForwards()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "work");
        _repo.Write("b.txt", "2");
        await _repo.CommitAsync("work");
        await _repo.RunAsync("switch", "main");
        var work = (await _service.GetBranchesAsync()).Single(b => b.Name == "work");

        var result = await _service.MergeAsync(work);

        Assert.True(result.Success, result.StdErr);
        Assert.Contains("Fast-forward", result.StdOut);
        Assert.Equal("work", (await _service.GetLogAsync())[0].Subject);
    }

    [Fact]
    public async Task MergeAsync_Conflict_ReportsConflictInStdout()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "work");
        _repo.Write("a.txt", "theirs");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.txt", "ours");
        await _repo.CommitAsync("ours");
        var work = (await _service.GetBranchesAsync()).Single(b => b.Name == "work");

        var result = await _service.MergeAsync(work);

        Assert.False(result.Success);
        Assert.Contains("CONFLICT", result.StdOut);
        Assert.True((await SingleFileAsync()).IsConflicted);
    }

    [Fact]
    public async Task DiscardWorktreeAsync_KeepsStagedPart()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        _repo.Write("a.txt", "staged");
        await _repo.RunAsync("add", "a.txt");
        _repo.Write("a.txt", "worktree");

        var result = await _service.DiscardWorktreeAsync([await SingleFileAsync()]);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal("staged", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
        Assert.Equal((ChangeKind.Modified, ChangeKind.Unmodified), ((await SingleFileAsync()).Staged, (await SingleFileAsync()).Unstaged));
    }

    [Fact]
    public async Task DiscardToHeadAsync_StagedNewAndModified_BackToHead()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        _repo.Write("a.txt", "2");
        _repo.Write("new file.txt", "x");
        await _repo.RunAsync("add", "-A");

        var result = await _service.DiscardToHeadAsync((await _service.GetStatusAsync()).Files);

        Assert.True(result.Success, result.StdErr);
        Assert.Empty((await _service.GetStatusAsync()).Files);
        Assert.Equal("1", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_repo.Path, "new file.txt")));
    }

    [Fact]
    public async Task DeleteUntrackedAsync_RemovesFileFromDisk()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        _repo.Write("dir/new.txt", "x");

        var result = await _service.DeleteUntrackedAsync([await SingleFileAsync()]);

        Assert.True(result.Success, result.StdErr);
        Assert.False(File.Exists(Path.Combine(_repo.Path, "dir", "new.txt")));
        Assert.Empty((await _service.GetStatusAsync()).Files);
    }

    [Fact]
    public async Task UndoAsync_LastCommit_ChangesBackInIndex()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("first");
        _repo.Write("b.txt", "2");
        await _repo.CommitAsync("second");

        var (plan, _) = UndoPlanner.Plan(await _service.GetLastReflogEntryAsync());
        var result = await _service.UndoAsync(plan!);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["first"], (await _service.GetLogAsync()).Select(c => c.Subject));
        Assert.Equal(ChangeKind.Added, (await SingleFileAsync()).Staged);
    }

    [Fact]
    public async Task UndoAsync_Merge_KeepsLocalChanges()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("first");
        await _repo.RunAsync("switch", "-c", "work");
        _repo.Write("b.txt", "2");
        await _repo.CommitAsync("work");
        await _repo.RunAsync("switch", "main");
        await _repo.RunAsync("merge", "--no-edit", "work");
        _repo.Write("a.txt", "local");

        var (plan, _) = UndoPlanner.Plan(await _service.GetLastReflogEntryAsync());
        var result = await _service.UndoAsync(plan!);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["first"], (await _service.GetLogAsync()).Select(c => c.Subject));
        Assert.Equal("local", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
        Assert.False(File.Exists(Path.Combine(_repo.Path, "b.txt")));
    }

    [Fact]
    public async Task GetLastReflogEntryAsync_EmptyRepository_ReturnsNull()
    {
        Assert.Null(await _service.GetLastReflogEntryAsync());
    }

    [Fact]
    public async Task StageAsync_DeletedFile_StagesDeletion()
    {
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
        File.Delete(Path.Combine(_repo.Path, "a.txt"));

        await _service.StageAsync(await SingleFileAsync());

        Assert.Equal(ChangeKind.Deleted, (await SingleFileAsync()).Staged);
    }

    [Fact]
    public async Task UnstageAsync_RenamedFile_UnstagesBothPaths()
    {
        _repo.Write("old.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("mv", "old.txt", "new.txt");

        await _service.UnstageAsync(await SingleFileAsync());

        var files = (await _service.GetStatusAsync()).Files;
        Assert.All(files, f => Assert.False(f.HasStagedChanges));
        Assert.Equal(ChangeKind.Deleted, files.Single(f => f.Path == "old.txt").Unstaged);
        Assert.True(files.Single(f => f.Path == "new.txt").IsUntracked);
    }

    [Fact]
    public async Task StageAllAndUnstageAll_AffectAllFiles()
    {
        _repo.Write("a.txt", "1");
        _repo.Write("b.txt", "2");

        await _service.StageAllAsync();
        var afterStage = (await _service.GetStatusAsync()).Files;
        await _service.UnstageAllAsync();
        var afterUnstage = (await _service.GetStatusAsync()).Files;

        Assert.All(afterStage, f => Assert.Equal(ChangeKind.Added, f.Staged));
        Assert.All(afterUnstage, f => Assert.True(f.IsUntracked));
    }

    [Fact]
    public async Task GetDiffAsync_StagedAndUnstaged_ReturnsSeparateChanges()
    {
        _repo.Write("a.txt", "1\n");
        await _repo.CommitAsync("init");
        _repo.Write("a.txt", "1\n2\n");
        await _repo.RunAsync("add", "a.txt");
        _repo.Write("a.txt", "1\n2\n3\n");
        var file = await SingleFileAsync();

        var staged = await _service.GetDiffAsync(file, staged: true);
        var unstaged = await _service.GetDiffAsync(file, staged: false);

        Assert.Equal(["2"], staged.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text));
        Assert.Equal(["3"], unstaged.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text));
    }

    [Fact]
    public async Task GetDiffAsync_UntrackedFile_ShowsWholeContentAsAdded()
    {
        Directory.CreateDirectory(Path.Combine(_repo.Path, "новая папка"));
        _repo.Write("новая папка/файл.cs", "namespace X;\n\nclass Y { }\n");
        var file = await SingleFileAsync();

        var diff = await _service.GetDiffAsync(file, staged: false);

        Assert.Equal("новая папка/файл.cs", file.Path);
        Assert.Equal(["namespace X;", "", "class Y { }"], diff.Where(l => l.Kind == DiffLineKind.Added).Select(l => l.Text));
    }

    [Fact]
    public async Task GetDiffAsync_StagedRename_ShowsRenameHeader()
    {
        _repo.Write("old.txt", "1\n");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("mv", "old.txt", "new.txt");

        var diff = await _service.GetDiffAsync(await SingleFileAsync(), staged: true);

        Assert.Contains(diff, l => l.Text == "rename from old.txt");
        Assert.Contains(diff, l => l.Text == "rename to new.txt");
    }

    [Fact]
    public async Task CommitAsync_MessageWithQuotesAndNewlines_IsKeptAsIs()
    {
        _repo.Write("a.txt", "1");
        await _service.StageAllAsync();
        const string message = "feat: add \"quoted\" text\n\n- body line with 'single' quotes";

        var result = await _service.CommitAsync(message);

        Assert.True(result.Success, result.StdErr);
        var log = await _repo.RunAsync("log", "-1", "--format=%B");
        Assert.Equal(message, log.StdOut.Trim());
        Assert.True((await _service.GetStatusAsync()).IsClean);
        Assert.Equal(7, (await _service.GetShortHeadAsync())!.Length);
    }

    [Fact]
    public async Task CommitAsync_NothingStaged_Fails()
    {
        _repo.Write("a.txt", "1");

        var result = await _service.CommitAsync("feat: nothing");

        Assert.False(result.Success);
    }

    private async Task<FileChange> SingleFileAsync() => Assert.Single((await _service.GetStatusAsync()).Files);
}
