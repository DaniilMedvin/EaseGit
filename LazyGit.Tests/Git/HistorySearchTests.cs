using LazyGit.Git;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

public sealed class HistorySearchTests : IAsyncLifetime
{
    private readonly TempRepository _repo = new();
    private GitService _service = null!;

    public async Task InitializeAsync()
    {
        await _repo.InitializeAsync();
        _service = new GitService(_repo.Git, _repo.Path);
        _repo.Write("a.cs", "class A { }\n");
        await _repo.CommitAsync("feat: add A.Load(1)");
        _repo.Write("a.cs", "class A { void Parse() { } }\n");
        await _repo.CommitAsync("feat: парсер для A");
        await _repo.RunAsync("switch", "-c", "side");
        _repo.Write("b.cs", "class B { }\n");
        await _repo.RunAsync("add", "b.cs");
        await _repo.RunAsync("-c", "user.name=Анна Иванова", "commit", "-m", "fix: side work");
        await _repo.RunAsync("switch", "main");
        _repo.Write("a.cs", "class A { }\n");
        await _repo.CommitAsync("refactor: убрать Parse");
    }

    public Task DisposeAsync() => _repo.DisposeAsync();

    private async Task<IEnumerable<string>> SubjectsAsync(string query, HistorySearchMode mode, bool all = false) =>
        (await _service.SearchLogAsync(query, mode, all)).Select(c => c.Subject);

    [Fact]
    public async Task Message_IgnoresCaseAndTreatsDotsLiterally()
    {
        Assert.Equal(["feat: add A.Load(1)"], await SubjectsAsync("a.load(", HistorySearchMode.Message));
        Assert.Equal(["feat: парсер для A"], await SubjectsAsync("парсер", HistorySearchMode.Message));
    }

    [Fact]
    public async Task Code_FindsWhereStringAppearedAndDisappeared()
    {
        Assert.Equal(["refactor: убрать Parse", "feat: парсер для A"], await SubjectsAsync("Parse()", HistorySearchMode.Code));
    }

    [Fact]
    public async Task Author_OnlyOtherBranchUnlessAllBranches()
    {
        Assert.Empty(await SubjectsAsync("Анна", HistorySearchMode.Author));
        Assert.Equal(["fix: side work"], await SubjectsAsync("Анна", HistorySearchMode.Author, all: true));
    }
}
