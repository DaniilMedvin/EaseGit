using LazyGit.Git;

namespace LazyGit.Tests;

/// <summary>Временный git-репозиторий для интеграционных тестов. Удаляется в DisposeAsync.</summary>
public sealed class TempRepository : IAsyncLifetime
{
    public GitRunner Git { get; } = new();

    public string Path { get; } = Directory.CreateTempSubdirectory("lazygit-tests-").FullName;

    public async Task InitializeAsync()
    {
        await RunAsync("init", "-b", "main");
        await RunAsync("config", "user.name", "Тест Юзер");
        await RunAsync("config", "user.email", "test@example.com");
        // Глобальные настройки пользователя не должны влиять на тесты.
        await RunAsync("config", "commit.gpgsign", "false");
    }

    public Task DisposeAsync()
    {
        Directory.Delete(Path, recursive: true);
        return Task.CompletedTask;
    }

    public void Write(string file, string content) => File.WriteAllText(System.IO.Path.Combine(Path, file), content);

    /// <summary>Выполняет git-команду и проверяет, что она завершилась успешно.</summary>
    public async Task<GitResult> RunAsync(params string[] args)
    {
        var result = await Git.RunAsync(Path, args);
        Assert.True(result.Success, $"{result.Command}: {result.StdErr}");
        return result;
    }

    public async Task CommitAsync(string message)
    {
        await RunAsync("add", "-A");
        await RunAsync("commit", "-m", message);
    }
}
