using EaseGit.Git;
using EaseGit.Models;
using EaseGit.UI;

namespace EaseGit.Tests.Git;

public sealed class StashTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.txt", "1");
        await _repo.CommitAsync("init");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    [Fact]
    public async Task StashAndPop_IncludesUntracked_RestoresEverything()
    {
        _repo.Write("a.txt", "2");
        _repo.Write("new file.txt", "x");

        var stash = await _service.StashAsync("моя работа");
        var afterStash = await _service.GetStatusAsync();
        var entry = Assert.Single(await _service.GetStashesAsync());
        var diff = await _service.GetStashDiffAsync(entry);
        var pop = await _service.StashPopAsync(entry);

        Assert.True(stash.Success, stash.StdErr);
        Assert.Empty(afterStash.Files);
        Assert.Equal("моя работа", entry.Message);
        Assert.Contains(diff.Lines, l => l.Kind == DiffLineKind.Added && l.Text == "x");
        Assert.True(pop.Success, pop.StdErr);
        Assert.Empty(await _service.GetStashesAsync());
        Assert.Equal(2, (await _service.GetStatusAsync()).Files.Count);
    }

    [Fact]
    public async Task ApplyAndDrop_ApplyKeepsEntry_DropRemovesIt()
    {
        _repo.Write("a.txt", "2");
        await _service.StashAsync("");
        var entry = Assert.Single(await _service.GetStashesAsync());

        await _service.StashApplyAsync(entry);
        var afterApply = await _service.GetStashesAsync();
        await _service.StashDropAsync(entry);

        Assert.Single(afterApply);
        Assert.StartsWith("WIP on main", entry.Message);
        Assert.Empty(await _service.GetStashesAsync());
        Assert.Equal("2", File.ReadAllText(Path.Combine(_repo.Path, "a.txt")));
    }

    [Fact]
    public async Task StashPopAsync_Conflict_KeepsStashAndExplains()
    {
        _repo.Write("a.txt", "stashed");
        await _service.StashAsync("");
        _repo.Write("a.txt", "committed");
        await _repo.CommitAsync("change");

        var result = await _service.StashPopAsync(Assert.Single(await _service.GetStashesAsync()));

        Assert.False(result.Success);
        Assert.StartsWith("Конфликт в stash", GitErrors.Explain(result));
        Assert.Single(await _service.GetStashesAsync());
    }

    [Fact]
    public async Task StashAsync_NoChanges_Explains()
    {
        var result = await _service.StashAsync("");

        Assert.Contains("Нечего сохранять", GitErrors.Explain(result));
    }
}
