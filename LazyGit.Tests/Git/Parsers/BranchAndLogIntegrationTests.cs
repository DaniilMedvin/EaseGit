using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git.Parsers;

/// <summary>Разбор реального вывода git for-each-ref и git log во временном репозитории.</summary>
public sealed class BranchAndLogIntegrationTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();

    public Task InitializeAsync() => _repo.InitializeAsync();

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task BranchParser_RealRepository_ReturnsBranchesAndCurrent()
    {
        _repo.Write("f.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("branch", "feature/diff");

        var branches = BranchParser.Parse((await _repo.RunAsync(BranchParser.Arguments)).StdOut);

        Assert.Equal(["feature/diff", "main"], branches.Select(b => b.Name));
        Assert.True(branches.Single(b => b.Name == "main").IsCurrent);
    }

    [Fact]
    public async Task LogParser_RealRepository_ReturnsNewestFirst()
    {
        _repo.Write("f.txt", "1");
        await _repo.CommitAsync("init: первый коммит");
        _repo.Write("f.txt", "2");
        await _repo.CommitAsync("fix: subject | with pipe");

        var commits = LogParser.Parse((await _repo.RunAsync(LogParser.Arguments(10))).StdOut);

        Assert.Equal(["fix: subject | with pipe", "init: первый коммит"], commits.Select(c => c.Subject));
        Assert.All(commits, c => Assert.Equal("Тест Юзер", c.AuthorName));
        Assert.All(commits, c => Assert.Equal(40, c.Hash.Length));
    }
}
