using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

public sealed class InteractiveRebaseTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("base.txt", "base\n");
        await _repo.CommitAsync("base");
        _repo.Write("a.txt", "a\n");
        await _repo.CommitAsync("add a");
        _repo.Write("b.txt", "b\n");
        await _repo.CommitAsync("add b");
        _repo.Write("c.txt", "c\n");
        await _repo.CommitAsync("add c");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    /// <summary>Три верхних коммита (c, b, a) как шаги pick, новые первыми.</summary>
    private async Task<List<RebaseStep>> TopStepsAsync() =>
        (await _service.GetLogAsync()).Take(3).Select(c => new RebaseStep(c, RebaseAction.Pick)).ToList();

    private async Task<List<string>> SubjectsAsync() => (await _service.GetLogAsync()).Select(c => c.Subject).ToList();

    [Fact]
    public async Task Rebase_Drop_RemovesCommitAndItsFile()
    {
        var steps = await TopStepsAsync();
        steps[1] = steps[1] with { Action = RebaseAction.Drop };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["add c", "add a", "base"], await SubjectsAsync());
        Assert.False(File.Exists(Path.Combine(_repo.Path, "b.txt")));
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
    }

    [Fact]
    public async Task Rebase_Fixup_JoinsChangesKeepingOlderMessage()
    {
        var steps = await TopStepsAsync();
        steps[0] = steps[0] with { Action = RebaseAction.Fixup };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["add b", "add a", "base"], await SubjectsAsync());
        var files = (await _repo.RunAsync("show", "--name-only", "--format=", "HEAD")).StdOut;
        Assert.Contains("b.txt", files);
        Assert.Contains("c.txt", files);
    }

    [Fact]
    public async Task Rebase_Squash_CombinesMessagesWithoutEditor()
    {
        var steps = await TopStepsAsync();
        steps[0] = steps[0] with { Action = RebaseAction.Squash };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        var message = (await _repo.RunAsync("log", "-1", "--format=%B")).StdOut;
        Assert.Contains("add b", message);
        Assert.Contains("add c", message);
        Assert.DoesNotContain("#", message);
    }

    [Fact]
    public async Task Rebase_Reword_ChangesSubjectAndKeepsBody()
    {
        _repo.Write("d.txt", "d\n");
        await _repo.RunAsync("add", "d.txt");
        await _repo.RunAsync("commit", "-m", "add d", "-m", "Тело с 'кавычками'");
        var steps = await TopStepsAsync();
        steps[0] = steps[0] with { Action = RebaseAction.Reword, NewSubject = "feat: файл d" };
        steps[2] = steps[2] with { Action = RebaseAction.Reword, NewSubject = "feat: файл b" };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["feat: файл d", "add c", "feat: файл b", "add a", "base"], await SubjectsAsync());
        Assert.Equal("feat: файл d\n\nТело с 'кавычками'", (await _repo.RunAsync("log", "-1", "--format=%B")).StdOut.Trim());
    }

    [Fact]
    public async Task Rebase_Reorder_ChangesCommitOrder()
    {
        var steps = await TopStepsAsync();
        (steps[0], steps[2]) = (steps[2], steps[0]);

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["add a", "add b", "add c", "base"], await SubjectsAsync());
    }

    [Fact]
    public async Task Rebase_Edit_StopsAndContinues()
    {
        var steps = await TopStepsAsync();
        steps[1] = steps[1] with { Action = RebaseAction.Edit };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(RepoOperation.Rebase, await _service.GetOperationAsync());
        Assert.Equal("add b", (await _service.GetLogAsync())[0].Subject);

        _repo.Write("b.txt", "b fixed\n");
        await _repo.RunAsync("add", "b.txt");
        Assert.True((await _service.AmendAsync()).Success);
        var resumed = await _service.ContinueAsync(RepoOperation.Rebase);

        Assert.True(resumed.Success, resumed.StdErr);
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
        Assert.Equal(["add c", "add b", "add a", "base"], await SubjectsAsync());
        Assert.Equal("b fixed\n", File.ReadAllText(Path.Combine(_repo.Path, "b.txt")));
    }

    [Fact]
    public async Task Rebase_Conflict_StopsThenContinuesAfterResolving()
    {
        _repo.Write("base.txt", "1\n");
        await _repo.CommitAsync("base 1");
        _repo.Write("base.txt", "2\n");
        await _repo.CommitAsync("base 2");
        // Без «base 1» изменение «1 → 2» не ложится на «base»: конфликт.
        var log = await _service.GetLogAsync();
        List<RebaseStep> steps = [new(log[0], RebaseAction.Pick), new(log[1], RebaseAction.Drop)];

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.False(result.Success);
        Assert.Equal(RepoOperation.Rebase, await _service.GetOperationAsync());
        _repo.Write("base.txt", "2\n");
        await _repo.RunAsync("add", "base.txt");
        var resumed = await _service.ContinueAsync(RepoOperation.Rebase);

        Assert.True(resumed.Success, resumed.StdErr);
        Assert.Equal(RepoOperation.None, await _service.GetOperationAsync());
        Assert.Equal(["base 2", "add c", "add b", "add a", "base"], await SubjectsAsync());
    }

    [Fact]
    public async Task Rebase_Abort_RestoresOriginalHistory()
    {
        var before = (await _service.GetLogAsync())[0].Hash;
        var steps = await TopStepsAsync();
        steps[2] = steps[2] with { Action = RebaseAction.Edit };
        await _service.InteractiveRebaseAsync(steps);

        var aborted = await _service.AbortAsync(RepoOperation.Rebase);

        Assert.True(aborted.Success, aborted.StdErr);
        Assert.Equal(before, (await _service.GetLogAsync())[0].Hash);
    }

    [Fact]
    public async Task Rebase_FromRootCommit_UsesRootOption()
    {
        var log = await _service.GetLogAsync();
        var steps = log.Select(c => new RebaseStep(c, RebaseAction.Pick)).ToList();
        steps[2] = steps[2] with { Action = RebaseAction.Fixup };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal(["add c", "add b", "base"], await SubjectsAsync());
        Assert.Contains("--root", GitService.InteractiveRebaseArguments(log[^1]));
    }

    [Fact]
    public async Task Rebase_DirtyWorktree_AutostashKeepsChanges()
    {
        _repo.Write("base.txt", "local edit\n");
        var steps = await TopStepsAsync();
        steps[0] = steps[0] with { Action = RebaseAction.Drop };

        var result = await _service.InteractiveRebaseAsync(steps);

        Assert.True(result.Success, result.StdErr);
        Assert.Equal("local edit\n", File.ReadAllText(Path.Combine(_repo.Path, "base.txt")));
    }
}
