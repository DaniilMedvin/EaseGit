using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

/// <summary>Построчный stage: патч из <see cref="PatchBuilder"/> применяется настоящим git apply --cached.</summary>
public sealed class LineStagingTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private async Task<FileChange> FileAsync(string path) =>
        (await _service.GetStatusAsync()).Files.Single(f => f.Path == path);

    /// <summary>Содержимое файла в индексе.</summary>
    private async Task<string> IndexAsync(string path) => (await _repo.RunAsync("show", ":" + path)).StdOut;

    /// <summary>Выбирает строки diff по условию и применяет патч к индексу.</summary>
    private async Task ApplyAsync(string path, bool staged, Func<DiffLine, bool> select)
    {
        var lines = await _service.GetDiffAsync(await FileAsync(path), staged);
        var selected = Enumerable.Range(0, lines.Count)
            .Where(i => lines[i].Kind is DiffLineKind.Added or DiffLineKind.Removed && select(lines[i]))
            .ToHashSet();
        var patch = PatchBuilder.Build(lines, selected, reverse: staged);
        Assert.NotNull(patch);

        var result = await _service.ApplyToIndexAsync(patch, reverse: staged);
        Assert.True(result.Success, result.StdErr + "\n" + patch);
    }

    [Fact]
    public async Task Stage_OneAddedLine_OnlyItGoesToIndex()
    {
        _repo.Write("a.txt", "1\n2\n3\n");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "1\nnew-a\n2\n3\nnew-b\n");

        await ApplyAsync("a.txt", staged: false, l => l.Text == "new-b");

        Assert.Equal("1\n2\n3\nnew-b\n", await IndexAsync("a.txt"));
    }

    [Fact]
    public async Task Stage_RemovedLineUnselected_StaysInIndex()
    {
        _repo.Write("a.txt", "1\nold\n3\n");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "1\nnew\n3\n");

        await ApplyAsync("a.txt", staged: false, l => l.Kind == DiffLineKind.Added);

        Assert.Equal("1\nold\nnew\n3\n", await IndexAsync("a.txt"));
    }

    [Fact]
    public async Task Stage_SecondHunkAfterSkippedFirst_AppliesAtRightPlace()
    {
        var lines = Enumerable.Range(1, 30).Select(i => i.ToString()).ToList();
        _repo.Write("a.txt", string.Join("\n", lines) + "\n");
        await _repo.CommitAsync("first");
        lines.Insert(1, "top");
        lines.Insert(2, "top2");
        lines[25] = "changed";
        _repo.Write("a.txt", string.Join("\n", lines) + "\n");

        await ApplyAsync("a.txt", staged: false, l => l.Text is "changed" or "24");

        var expected = Enumerable.Range(1, 30).Select(i => i == 24 ? "changed" : i.ToString());
        Assert.Equal(string.Join("\n", expected) + "\n", await IndexAsync("a.txt"));
    }

    [Fact]
    public async Task Stage_CrlfFile_KeepsLineEndings()
    {
        await _repo.RunAsync("config", "core.autocrlf", "false");
        _repo.Write("a.txt", "1\r\n2\r\n");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "1\r\nx\r\n2\r\ny\r\n");

        await ApplyAsync("a.txt", staged: false, l => l.Text == "x");

        Assert.Equal("1\r\nx\r\n2\r\n", await IndexAsync("a.txt"));
    }

    [Fact]
    public async Task Stage_PartOfUntrackedFile_CreatesFileInIndex()
    {
        _repo.Write("base.txt", "base\n");
        await _repo.CommitAsync("first");
        _repo.Write("Новая папка/new file.txt", "a\nb\nc\n");

        await ApplyAsync("Новая папка/new file.txt", staged: false, l => l.Text != "b");

        Assert.Equal("a\nc\n", await IndexAsync("Новая папка/new file.txt"));
    }

    [Fact]
    public async Task Stage_PartOfDeletedFile_KeepsFileWithRestLines()
    {
        _repo.Write("a.txt", "1\n2\n3\n");
        await _repo.CommitAsync("first");
        File.Delete(Path.Combine(_repo.Path, "a.txt"));

        await ApplyAsync("a.txt", staged: false, l => l.Text == "2");

        Assert.Equal("1\n3\n", await IndexAsync("a.txt"));
    }

    [Fact]
    public async Task Unstage_OneLine_ReturnsItToWorktreeOnly()
    {
        _repo.Write("a.txt", "1\n2\n");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "1\nx\n2\ny\n");
        await _repo.RunAsync("add", "a.txt");

        await ApplyAsync("a.txt", staged: true, l => l.Text == "x");

        Assert.Equal("1\n2\ny\n", await IndexAsync("a.txt"));
        Assert.Equal("1\nx\n2\ny\n", await File.ReadAllTextAsync(Path.Combine(_repo.Path, "a.txt")));
    }

    [Fact]
    public async Task Unstage_PartOfNewFile_KeepsFileInIndex()
    {
        _repo.Write("base.txt", "base\n");
        await _repo.CommitAsync("first");
        _repo.Write("n.txt", "a\nb\n");
        await _repo.RunAsync("add", "n.txt");

        await ApplyAsync("n.txt", staged: true, l => l.Text == "a");

        Assert.Equal("b\n", await IndexAsync("n.txt"));
    }

    [Fact]
    public async Task Unstage_WholeNewFile_RemovesItFromIndex()
    {
        _repo.Write("base.txt", "base\n");
        await _repo.CommitAsync("first");
        _repo.Write("n.txt", "a\nb\n");
        await _repo.RunAsync("add", "n.txt");

        await ApplyAsync("n.txt", staged: true, _ => true);

        var file = await FileAsync("n.txt");
        Assert.True(file.IsUntracked);
    }

    [Fact]
    public async Task Stage_NoNewlineAtEnd_AddedLastLine()
    {
        _repo.Write("a.txt", "1\n2");
        await _repo.CommitAsync("first");
        _repo.Write("a.txt", "0\n1\n2");

        await ApplyAsync("a.txt", staged: false, l => l.Text == "0");

        Assert.Equal("0\n1\n2", await IndexAsync("a.txt"));
    }

    [Fact]
    public void Build_NothingSelected_ReturnsNull()
    {
        var lines = new List<DiffLine>
        {
            new(DiffLineKind.Header, "diff --git a/a.txt b/a.txt"),
            new(DiffLineKind.Hunk, "@@ -1 +1 @@"),
            new(DiffLineKind.Removed, "a", OldNumber: 1),
            new(DiffLineKind.Added, "b", NewNumber: 1),
        };

        Assert.Null(PatchBuilder.Build(lines, new HashSet<int>(), reverse: false));
    }
}
