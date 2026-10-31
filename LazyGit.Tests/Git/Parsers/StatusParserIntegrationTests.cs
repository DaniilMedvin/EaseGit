using LazyGit.Git;
using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Tests.Git.Parsers;

/// <summary>Разбор реального вывода git во временном репозитории.</summary>
public sealed class StatusParserIntegrationTests : IAsyncLifetime
{
    private readonly GitRunner _git = new();
    private readonly string _repo = Directory.CreateTempSubdirectory("lazygit-tests-").FullName;

    public async Task InitializeAsync()
    {
        await Git("init", "-b", "main");
        await Git("config", "user.name", "Test");
        await Git("config", "user.email", "test@example.com");
        // Глобальные настройки пользователя не должны влиять на тест.
        await Git("config", "commit.gpgsign", "false");
    }

    public Task DisposeAsync()
    {
        Directory.Delete(_repo, recursive: true);
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Parse_RealRepository_ReturnsAllChangeKinds()
    {
        Write("modified.txt", "1");
        Write("old name.txt", "2");
        await Git("add", "-A");
        await Git("commit", "-m", "init");

        Write("modified.txt", "1\n2");
        await Git("add", "modified.txt");
        Write("modified.txt", "1\n2\n3");
        await Git("mv", "old name.txt", "новое имя.txt");
        Write("untracked файл.cs", "x");

        var result = await _git.RunAsync(_repo, StatusParser.Arguments);
        var status = StatusParser.Parse(result.StdOut);

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

    private void Write(string path, string content) => File.WriteAllText(Path.Combine(_repo, path), content);

    private async Task Git(params string[] args)
    {
        var result = await _git.RunAsync(_repo, args);
        Assert.True(result.Success, result.StdErr);
    }
}
