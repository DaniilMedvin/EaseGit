using EaseGit.Git.Parsers;
using EaseGit.Models;

namespace EaseGit.Tests.Git.Parsers;

/// <summary>Разбор реального вывода git status во временном репозитории.</summary>
public sealed class StatusParserIntegrationTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();

    public Task InitializeAsync() => _repo.InitializeAsync();

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task Parse_RealRepository_ReturnsAllChangeKinds()
    {
        _repo.Write("modified.txt", "1");
        _repo.Write("old name.txt", "2");
        await _repo.CommitAsync("init");

        _repo.Write("modified.txt", "1\n2");
        await _repo.RunAsync("add", "modified.txt");
        _repo.Write("modified.txt", "1\n2\n3");
        await _repo.RunAsync("mv", "old name.txt", "новое имя.txt");
        _repo.Write("untracked файл.cs", "x");

        var status = StatusParser.Parse((await _repo.RunAsync(StatusParser.Arguments)).StdOut);

        Assert.Equal("main", status.Branch);
        Assert.NotNull(status.HeadHash);

        var modified = status.Files.Single(f => f.Path == "modified.txt");
        Assert.Equal(ChangeKind.Modified, modified.Staged);
        Assert.Equal(ChangeKind.Modified, modified.Unstaged);

        var renamed = status.Files.Single(f => f.Path == "новое имя.txt");
        Assert.Equal(ChangeKind.Renamed, renamed.Staged);
        Assert.Equal("old name.txt", renamed.OriginalPath);

        Assert.True(status.Files.Single(f => f.Path == "untracked файл.cs").IsUntracked);
    }
}
