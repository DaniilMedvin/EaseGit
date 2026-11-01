using LazyGit.Git;
using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git;

public sealed class CompareTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("old.cs", "a\nb\nc\nd\n");
        _repo.Write("keep.txt", "k\n");
        await _repo.CommitAsync("base");
        await _repo.RunAsync("switch", "-c", "feature");
        await _repo.RunAsync("mv", "old.cs", "Новый.cs");
        _repo.Write("Новый.cs", "a\nb2\nc\nd\n");
        _repo.Write("img.bin", "\0\x01\x02");
        await _repo.CommitAsync("feature work");
        await _repo.RunAsync("switch", "main");
        _repo.Write("keep.txt", "k2\n");
        await _repo.CommitAsync("main work");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public void Parse_Sample_MergesStatusAndCounts()
    {
        var files = CompareParser.Parse("R054\0old.cs\0New.cs\0A\0img.bin\0M\0a b.txt\0", "2\t1\t\0old.cs\0New.cs\0-\t-\timg.bin\0" + "3\t0\ta b.txt\0");

        Assert.Equal(('R', "New.cs", "old.cs", 2, 1), (files[0].Status, files[0].Path, files[0].OldPath, files[0].Added, files[0].Deleted));
        Assert.Null(files[1].Added);
        Assert.Equal(3, files[2].Added);
    }

    [Fact]
    public async Task Compare_FromMergeBase_ShowsOnlyWhatFeatureBrought()
    {
        var files = await _service.CompareFilesAsync("main", "feature", fromMergeBase: true);

        Assert.Equal(["img.bin", "Новый.cs"], files.Select(f => f.Path).Order(StringComparer.Ordinal));
        var renamed = files.Single(f => f.Path == "Новый.cs");
        Assert.Equal(('R', "old.cs", 1, 1), (renamed.Status, renamed.OldPath, renamed.Added, renamed.Deleted));
        Assert.Null(files.Single(f => f.Path == "img.bin").Added);
        Assert.Contains((await _service.CompareFileDiffAsync("main", "feature", true, renamed)), l => l.Text == "b2");
    }

    [Fact]
    public async Task Compare_BetweenTips_AlsoShowsMainChanges()
    {
        var files = await _service.CompareFilesAsync("main", "feature", fromMergeBase: false);

        Assert.Contains(files, f => f.Path == "keep.txt");
    }

    [Fact]
    public async Task Divergence_CommitsAndMergeBase()
    {
        Assert.Equal((1, 1), await _service.CountDivergenceAsync("main", "feature"));
        Assert.Equal(["feature work"], (await _service.CommitsOnlyInAsync("main", "feature")).Select(c => c.Subject));
        Assert.NotNull(await _service.GetMergeBaseAsync("main", "feature"));
    }
}
