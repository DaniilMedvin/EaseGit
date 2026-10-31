using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Git;

/// <summary>
/// Операции с одним репозиторием.
/// Методы чтения при ошибке git бросают <see cref="GitCommandException"/>.
/// Методы изменения возвращают <see cref="GitResult"/>, чтобы интерфейс мог показать текст ошибки.
/// </summary>
public sealed class GitService
{
    public const int DefaultLogLimit = 200;

    private readonly GitRunner _git;

    public GitService(GitRunner git, string repoRoot)
    {
        _git = git;
        RepoRoot = repoRoot;
    }

    /// <summary>Абсолютный путь к корню репозитория.</summary>
    public string RepoRoot { get; }

    /// <summary>Открывает репозиторий, содержащий <paramref name="path"/>, или возвращает null.</summary>
    public static async Task<GitService?> OpenAsync(GitRunner git, string path, CancellationToken ct = default)
    {
        var root = await git.FindRepoRootAsync(path, ct);
        return root is null ? null : new GitService(git, root);
    }

    /// <summary>Создаёт репозиторий в папке <paramref name="path"/>; несуществующая папка создаётся.</summary>
    public static async Task<GitResult> InitAsync(GitRunner git, string path, CancellationToken ct = default)
    {
        Directory.CreateDirectory(path);
        return await git.RunAsync(path, ["init"], ct);
    }

    public async Task<RepoStatus> GetStatusAsync(CancellationToken ct = default) =>
        StatusParser.Parse(await RunCheckedAsync(StatusParser.Arguments, ct));

    public async Task<IReadOnlyList<Branch>> GetBranchesAsync(CancellationToken ct = default) =>
        BranchParser.Parse(await RunCheckedAsync(BranchParser.Arguments, ct));

    /// <summary>Последние коммиты текущей ветки, новые первыми. В репозитории без коммитов — пустой список.</summary>
    public async Task<IReadOnlyList<Commit>> GetLogAsync(int limit = DefaultLogLimit, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, LogParser.Arguments(limit), ct);
        if (result.Success)
            return LogParser.Parse(result.StdOut);

        if (!await HasCommitsAsync(ct))
            return [];

        throw new GitCommandException(result);
    }

    /// <summary>Есть ли в репозитории хотя бы один коммит (HEAD указывает на коммит).</summary>
    public async Task<bool> HasCommitsAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["rev-parse", "--verify", "--quiet", "HEAD"], ct);
        return result.Success;
    }

    /// <summary>Добавляет изменения файла в индекс, включая удаление и новые файлы.</summary>
    public Task<GitResult> StageAsync(FileChange file, CancellationToken ct = default) =>
        _git.RunAsync(RepoRoot, ["add", "--", .. Paths(file)], ct);

    /// <summary>Убирает изменения файла из индекса, рабочая копия не меняется.</summary>
    public async Task<GitResult> UnstageAsync(FileChange file, CancellationToken ct = default)
    {
        // Без коммитов restore --staged не работает: сравнивать индекс не с чем.
        string[] args = await HasCommitsAsync(ct)
            ? ["restore", "--staged", "--", .. Paths(file)]
            : ["rm", "--cached", "--quiet", "--", .. Paths(file)];
        return await _git.RunAsync(RepoRoot, args, ct);
    }

    public Task<GitResult> StageAllAsync(CancellationToken ct = default) =>
        _git.RunAsync(RepoRoot, ["add", "--all"], ct);

    public Task<GitResult> UnstageAllAsync(CancellationToken ct = default) =>
        _git.RunAsync(RepoRoot, ["reset", "--quiet"], ct);

    /// <summary>
    /// Diff файла: <paramref name="staged"/> — изменения в индексе, иначе — в рабочей копии.
    /// Для untracked-файла всё содержимое показывается как добавленное.
    /// </summary>
    public async Task<IReadOnlyList<DiffLine>> GetDiffAsync(FileChange file, bool staged, CancellationToken ct = default)
    {
        if (file.IsUntracked)
        {
            // С --no-index git возвращает 1, если файлы различаются, — это не ошибка.
            var untracked = await _git.RunAsync(RepoRoot, ["diff", "--no-color", "--no-ext-diff", "--no-index", "--", "/dev/null", file.Path], ct);
            return untracked.ExitCode is 0 or 1 ? DiffParser.Parse(untracked.StdOut) : throw new GitCommandException(untracked);
        }

        string[] args = staged
            ? ["diff", "--no-color", "--no-ext-diff", "--find-renames", "--cached", "--", .. Paths(file)]
            : ["diff", "--no-color", "--no-ext-diff", "--", file.Path];
        return DiffParser.Parse(await RunCheckedAsync(args, ct));
    }

    /// <summary>Создаёт коммит из изменений в индексе. Сообщение передаётся через stdin.</summary>
    public Task<GitResult> CommitAsync(string message, CancellationToken ct = default) =>
        _git.RunAsync(RepoRoot, ["commit", "--file=-", "--cleanup=strip"], ct, input: message);

    /// <summary>Короткий хеш HEAD или null в репозитории без коммитов.</summary>
    public async Task<string?> GetShortHeadAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["rev-parse", "--short", "HEAD"], ct);
        return result.Success ? result.StdOut.Trim() : null;
    }

    /// <summary>Пути файла. Для переименования — оба, иначе удаление старого пути останется в индексе.</summary>
    private static string[] Paths(FileChange file) =>
        file.OriginalPath is null ? [file.Path] : [file.Path, file.OriginalPath];

    private async Task<string> RunCheckedAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await _git.RunAsync(RepoRoot, args, ct);
        return result.Success ? result.StdOut : throw new GitCommandException(result);
    }
}
