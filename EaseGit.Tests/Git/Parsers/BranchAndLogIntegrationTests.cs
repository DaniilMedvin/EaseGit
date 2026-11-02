using EaseGit.Git.Parsers;
using EaseGit.Models;
using EaseGit.UI.Panels;

namespace EaseGit.Tests.Git.Parsers;

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

    [Fact]
    public async Task LogParser_RealMerge_GraphHasTwoLanes()
    {
        _repo.Write("f.txt", "1");
        await _repo.CommitAsync("init");
        await _repo.RunAsync("checkout", "-b", "feature");
        _repo.Write("g.txt", "feature");
        await _repo.CommitAsync("feature work");
        await _repo.RunAsync("checkout", "main");
        _repo.Write("f.txt", "2");
        await _repo.CommitAsync("main work");
        await _repo.RunAsync("tag", "v1");
        await _repo.RunAsync("merge", "--no-ff", "--no-edit", "feature");

        var commits = LogParser.Parse((await _repo.RunAsync(LogParser.Arguments(10))).StdOut);
        var graph = CommitGraph.Build(commits).Select(r => new string(r.Select(c => c.Symbol).ToArray())).ToList();

        Assert.Equal(2, commits[0].Parents.Count);
        Assert.Contains(commits[0].Refs, r => r is { Name: "main", IsCurrent: true });
        Assert.Contains(commits, c => c.Refs.Contains(new CommitRef("v1", RefKind.Tag)));
        Assert.Contains(commits, c => c.Refs.Contains(new CommitRef("feature", RefKind.Branch)));
        Assert.Equal("◉─╮ ", graph[0]);
        Assert.Equal("●─╯ ", graph[^1]);
    }
}
