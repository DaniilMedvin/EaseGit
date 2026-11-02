using EaseGit.Git;
using EaseGit.Git.Parsers;
using EaseGit.Models;
using EaseGit.UI.Panels;

namespace EaseGit.Tests.Git;

public sealed class CodeChangesIntegrationTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("A.cs", "class A { void M() { } }\n");
        _repo.Write("notes.txt", "x\n");
        await _repo.CommitAsync("first");
        _repo.Write("A.cs", "class A { void M() { Run(); } void N() { } }\n");
        _repo.Write("notes.txt", "y\n");
        await _repo.CommitAsync("second");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private static List<string> Names(IReadOnlyList<CodeChange> changes) => changes.Select(c => $"{c.Kind} {c.Name}").ToList();

    [Fact]
    public async Task Commit_OnlyCSharpFilesWithChanges()
    {
        var log = await _service.GetLogAsync();

        var second = await _service.GetCodeChangesAsync(log[0].Parents[0], log[0].Hash);
        var first = await _service.GetCodeChangesAsync(GitService.EmptyTree, log[1].Hash);

        var (path, changes) = Assert.Single(second);
        Assert.Equal("A.cs", path);
        Assert.Equal(["Modified A.M()", "Added A.N()"], Names(changes));
        Assert.Equal(["Added A"], Names(Assert.Single(first).Changes));
    }

    [Fact]
    public async Task WorkingTreeAndStaged_CompareRightVersions()
    {
        _repo.Write("A.cs", "class A { void M() { Run(); } void N() { } void O() { } }\n");
        await _repo.RunAsync("add", "A.cs");
        _repo.Write("A.cs", "class A { void M() { Run(); } void O() { } }\n");
        var file = (await _service.GetStatusAsync()).Files.Single();

        Assert.Equal(["Added A.O()"], Names(await _service.GetCodeChangesAsync(file, staged: true)));
        Assert.Equal(["Removed A.N()"], Names(await _service.GetCodeChangesAsync(file, staged: false)));
    }

    [Fact]
    public void WithCodeChanges_InCommitGoesBeforeStat()
    {
        var commit = new Commit("abc1234", "a", "a@b", DateTimeOffset.Now, "s");
        var diff = new CommitDiff("s", DiffParser.Parse("diff --git a/A.cs b/A.cs\n--- a/A.cs\n+++ b/A.cs\n@@ -1 +1 @@\n-a\n+b\n"));
        var prepared = DiffPanel.PrepareCommit(commit, diff, DateTimeOffset.Now);

        var withChanges = DiffPanel.WithCodeChanges(prepared, [("A.cs", [new CodeChange(CodeChangeKind.Added, "метод", "A.N()")])]);

        var lines = withChanges.Lines.ToList();
        Assert.True(lines.FindIndex(l => l.Kind == DiffLineKind.CodeChange) < lines.FindIndex(l => l.Kind == DiffLineKind.Stat));
        Assert.Equal(lines.Count, withChanges.Highlights.Length);
    }
}
