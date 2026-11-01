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

    /// <summary>Файл из двух частей, меняющихся в разных ветках, — два блока конфликта; CRLF и BOM.</summary>
    private async Task MergeWithTwoBlocksAsync()
    {
        const string bom = "\uFEFF";
        _repo.Write("code.cs", bom + "a\r\n1\r\nx\r\nx\r\nx\r\nb\r\n1\r\n");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("switch", "-c", "other");
        _repo.Write("code.cs", bom + "a\r\n3\r\nx\r\nx\r\nx\r\nb\r\n3\r\n");
        await _repo.CommitAsync("theirs");
        await _repo.RunAsync("switch", "main");
        _repo.Write("code.cs", bom + "a\r\n2\r\nx\r\nx\r\nx\r\nb\r\n2\r\n");
        await _repo.CommitAsync("ours");
        await _repo.Git.RunAsync(_repo.Path, ["merge", "other"]);
    }

    [Fact]
    public async Task ReadConflictFile_TwoBlocks_ResolveEachKeepsBomAndCrlf()
    {
        await MergeWithTwoBlocksAsync();

        var (file, problem) = await _service.ReadConflictFileAsync("code.cs");

        Assert.Null(problem);
        Assert.Equal(2, file!.Blocks);
        Assert.True(file.HasBom);
        var parts = ConflictResolver.Resolve(file.Parts, 0, ConflictChoice.Theirs);
        parts = ConflictResolver.Resolve(parts, 0, ConflictChoice.Ours);
        await _service.WriteConflictFileAsync(file, parts);

        var bytes = File.ReadAllBytes(Path.Combine(_repo.Path, "code.cs"));
        Assert.Equal([0xEF, 0xBB, 0xBF], bytes[..3]);
        Assert.Equal("a\r\n3\r\nx\r\nx\r\nx\r\nb\r\n2\r\n", System.Text.Encoding.UTF8.GetString(bytes[3..]));
    }

    [Fact]
    public async Task RestoreConflict_AfterResolving_MarkersAreBack()
    {
        await MergeWithConflictAsync();
        var (file, _) = await _service.ReadConflictFileAsync("a.txt");
        await _service.WriteConflictFileAsync(file!, ConflictResolver.Resolve(file!.Parts, 0, ConflictChoice.Ours));

        var result = await _service.RestoreConflictAsync("a.txt");

        Assert.True(result.Success, result.StdErr);
        Assert.Contains("<<<<<<<", Read("a.txt"));
    }

    [Fact]
    public async Task ReadConflictFile_NotUtf8OrNoMarkers_ExplainsWhy()
    {
        await MergeWithConflictAsync();
        File.WriteAllBytes(Path.Combine(_repo.Path, "bin.dat"), [0xFF, 0xFE, 0x00, 0xC3]);
        _repo.Write("plain.txt", "no markers\n");

        Assert.Contains("UTF-8", (await _service.ReadConflictFileAsync("bin.dat")).Problem);
        Assert.Contains("<<<<<<<", (await _service.ReadConflictFileAsync("plain.txt")).Problem);
        Assert.NotNull((await _service.ReadConflictFileAsync("missing.txt")).Problem);
    }
}
