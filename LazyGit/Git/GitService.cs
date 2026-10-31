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
    private readonly CommandLog? _log;

    /// <param name="log">Журнал, куда пишутся изменяющие команды; null — не вести.</param>
    public GitService(GitRunner git, string repoRoot, CommandLog? log = null)
    {
        _git = git;
        RepoRoot = repoRoot;
        _log = log;
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
        StageAsync([file], ct);

    /// <summary>Добавляет изменения нескольких файлов (например, папки) в индекс одной командой.</summary>
    public Task<GitResult> StageAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default) =>
        RunActionAsync(["add", "--", .. files.SelectMany(Paths)], ct);

    /// <summary>Убирает изменения файла из индекса, рабочая копия не меняется.</summary>
    public Task<GitResult> UnstageAsync(FileChange file, CancellationToken ct = default) =>
        UnstageAsync([file], ct);

    /// <summary>Убирает изменения нескольких файлов из индекса одной командой.</summary>
    public async Task<GitResult> UnstageAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default)
    {
        var paths = files.SelectMany(Paths);
        // Без коммитов restore --staged не работает: сравнивать индекс не с чем.
        string[] args = await HasCommitsAsync(ct)
            ? ["restore", "--staged", "--", .. paths]
            : ["rm", "--cached", "--quiet", "--", .. paths];
        return await RunActionAsync(args, ct);
    }

    /// <summary>Аргументы отмены изменений рабочей копии: файлы возвращаются к версии из индекса.</summary>
    public static string[] DiscardWorktreeArguments(IReadOnlyList<FileChange> files) =>
        ["restore", "--", .. files.Select(f => f.Path)];

    /// <summary>Аргументы полного сброса к HEAD: индекс и рабочая копия. Добавленный файл удаляется.</summary>
    public static string[] DiscardToHeadArguments(IReadOnlyList<FileChange> files) =>
        ["restore", "--source=HEAD", "--staged", "--worktree", "--", .. files.SelectMany(Paths)];

    /// <summary>Аргументы удаления untracked-файлов с диска.</summary>
    public static string[] DeleteUntrackedArguments(IReadOnlyList<FileChange> files) =>
        ["clean", "--force", "--", .. files.Select(f => f.Path)];

    /// <summary>Отменяет изменения рабочей копии, staged-изменения сохраняются. Необратимо.</summary>
    public Task<GitResult> DiscardWorktreeAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default) =>
        RunActionAsync(DiscardWorktreeArguments(files), ct);

    /// <summary>Сбрасывает файлы к HEAD: и индекс, и рабочую копию. Необратимо.</summary>
    public Task<GitResult> DiscardToHeadAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default) =>
        RunActionAsync(DiscardToHeadArguments(files), ct);

    /// <summary>Удаляет untracked-файлы с диска. Необратимо.</summary>
    public Task<GitResult> DeleteUntrackedAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default) =>
        RunActionAsync(DeleteUntrackedArguments(files), ct);

    public Task<GitResult> StageAllAsync(CancellationToken ct = default) =>
        RunActionAsync(["add", "--all"], ct);

    public Task<GitResult> UnstageAllAsync(CancellationToken ct = default) =>
        RunActionAsync(["reset", "--quiet"], ct);

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

    /// <summary>Сообщение и изменения коммита. Для первого коммита diff — относительно пустого дерева.</summary>
    public async Task<CommitDiff> GetCommitDiffAsync(Commit commit, CancellationToken ct = default) =>
        CommitDiffParser.Parse(await RunCheckedAsync(CommitDiffParser.Arguments(commit.Hash), ct));

    /// <summary>Переключается на ветку. Незакоммиченные изменения переносятся, если не конфликтуют.</summary>
    public Task<GitResult> SwitchAsync(Branch branch, CancellationToken ct = default) =>
        RunActionAsync(["switch", "--no-guess", branch.Name], ct);

    /// <summary>Создаёт ветку от HEAD и переключается на неё. В репозитории без коммитов — просто меняет имя ветки.</summary>
    public Task<GitResult> CreateBranchAsync(string name, CancellationToken ct = default) =>
        RunActionAsync(["switch", "--create", name], ct);

    /// <summary>
    /// Аргументы удаления ветки: без <paramref name="force"/> git откажет, если в ветке есть невлитые коммиты.
    /// Отдельный метод, чтобы показать команду в подтверждении до выполнения.
    /// </summary>
    public static string[] DeleteBranchArguments(Branch branch, bool force) =>
        ["branch", force ? "-D" : "-d", branch.Name];

    public Task<GitResult> DeleteBranchAsync(Branch branch, bool force, CancellationToken ct = default) =>
        RunActionAsync(DeleteBranchArguments(branch, force), ct);

    /// <summary>Сколько коммитов ветки нет в текущей (HEAD): они пропадут при принудительном удалении.</summary>
    public async Task<int> CountUnmergedAsync(Branch branch, CancellationToken ct = default)
    {
        var output = await RunCheckedAsync(["rev-list", "--count", $"HEAD..{branch.Name}"], ct);
        return int.TryParse(output.Trim(), out var count) ? count : 0;
    }

    /// <summary>Вливает ветку в текущую. При конфликте git возвращает 1, а список конфликтов пишет в stdout.</summary>
    public Task<GitResult> MergeAsync(Branch branch, CancellationToken ct = default) =>
        RunActionAsync(["merge", "--no-edit", branch.Name], ct);

    /// <summary>Скачивает новые коммиты со всех remote и убирает удалённые на сервере ветки. Рабочая копия не меняется.</summary>
    public Task<GitResult> FetchAsync(CancellationToken ct = default) =>
        RunActionAsync(["fetch", "--all", "--prune"], ct);

    /// <summary>Pull текущей ветки. --no-edit: для merge-коммита git не открывает редактор.</summary>
    public Task<GitResult> PullAsync(CancellationToken ct = default) =>
        RunActionAsync(["pull", "--no-edit"], ct);

    /// <summary>Push текущей ветки в её upstream.</summary>
    public Task<GitResult> PushAsync(CancellationToken ct = default) =>
        RunActionAsync(["push"], ct);

    /// <summary>Аргументы первого push ветки: публикует её на remote и настраивает upstream.</summary>
    public static string[] PushSetUpstreamArguments(string remote, string branch) =>
        ["push", "--set-upstream", remote, branch];

    public Task<GitResult> PushSetUpstreamAsync(string remote, string branch, CancellationToken ct = default) =>
        RunActionAsync(PushSetUpstreamArguments(remote, branch), ct);

    /// <summary>Имена remote: origin первым, если он есть.</summary>
    public async Task<IReadOnlyList<string>> GetRemotesAsync(CancellationToken ct = default)
    {
        var output = await RunCheckedAsync(["remote"], ct);
        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderBy(r => r == "origin" ? 0 : 1)
            .ToList();
    }

    /// <summary>Последнее действие из reflog HEAD или null (нет коммитов или reflog выключен).</summary>
    public async Task<ReflogEntry?> GetLastReflogEntryAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ReflogParser.Arguments(1), ct);
        return result.Success ? ReflogParser.Parse(result.StdOut).FirstOrDefault() : null;
    }

    /// <summary>Есть ли HEAD уже в upstream текущей ветки (коммит отправлен). Без upstream — false.</summary>
    public async Task<bool> IsHeadPushedAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["merge-base", "--is-ancestor", "HEAD", "@{upstream}"], ct);
        return result.Success;
    }

    /// <summary>Выполняет план отмены последнего действия.</summary>
    public Task<GitResult> UndoAsync(UndoPlan plan, CancellationToken ct = default) =>
        RunActionAsync(plan.Args, ct);

    /// <summary>Создаёт коммит из изменений в индексе. Сообщение передаётся через stdin.</summary>
    public Task<GitResult> CommitAsync(string message, CancellationToken ct = default) =>
        RunActionAsync(["commit", "--file=-", "--cleanup=strip"], ct, input: message);

    /// <summary>Короткий хеш HEAD или null в репозитории без коммитов.</summary>
    public async Task<string?> GetShortHeadAsync(CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["rev-parse", "--short", "HEAD"], ct);
        return result.Success ? result.StdOut.Trim() : null;
    }

    /// <summary>Пути файла. Для переименования — оба, иначе удаление старого пути останется в индексе.</summary>
    private static string[] Paths(FileChange file) =>
        file.OriginalPath is null ? [file.Path] : [file.Path, file.OriginalPath];

    /// <summary>Выполняет изменяющую команду и записывает её в журнал, в том числе отменённую.</summary>
    private async Task<GitResult> RunActionAsync(IReadOnlyList<string> args, CancellationToken ct, string? input = null)
    {
        var started = DateTimeOffset.Now;
        try
        {
            var result = await _git.RunAsync(RepoRoot, args, ct, input);
            var output = (result.StdErr.Trim() + "\n" + result.StdOut.Trim()).Trim();
            _log?.Add(new CommandLogEntry(started, args, result.ExitCode, DateTimeOffset.Now - started, output));
            return result;
        }
        catch (OperationCanceledException)
        {
            _log?.Add(new CommandLogEntry(started, args, null, DateTimeOffset.Now - started, ""));
            throw;
        }
    }

    private async Task<string> RunCheckedAsync(IReadOnlyList<string> args, CancellationToken ct)
    {
        var result = await _git.RunAsync(RepoRoot, args, ct);
        return result.Success ? result.StdOut : throw new GitCommandException(result);
    }
}
