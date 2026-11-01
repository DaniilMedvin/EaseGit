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

    /// <summary>Сколько коммитов показывает окно графа всех веток.</summary>
    public const int GraphLogLimit = 1000;

    private readonly GitRunner _git;
    private readonly CommandLog? _log;
    private string? _gitDir;

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

    /// <summary>
    /// Последние коммиты текущей ветки (или с <paramref name="allBranches"/> — всех веток и тегов), новые первыми.
    /// В репозитории без коммитов — пустой список.
    /// </summary>
    public async Task<IReadOnlyList<Commit>> GetLogAsync(int limit = DefaultLogLimit, CancellationToken ct = default,
        bool allBranches = false)
    {
        var result = await _git.RunAsync(RepoRoot, LogParser.Arguments(limit, allBranches), ct);
        if (result.Success)
            return LogParser.Parse(result.StdOut);

        if (!await HasCommitsAsync(ct))
            return [];

        throw new GitCommandException(result);
    }

    /// <summary>git log --graph ветки <paramref name="branch"/>, новые сверху: для просмотра ветки без checkout.</summary>
    public async Task<IReadOnlyList<GraphLogLine>> GetBranchGraphLogAsync(Branch branch, int limit = DefaultLogLimit,
        CancellationToken ct = default) =>
        GraphLogParser.Parse(await RunCheckedAsync(GraphLogParser.Arguments(branch.Name, limit), ct));

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
            var untracked = await _git.RunAsync(RepoRoot, ["diff", .. DiffOptions, "--no-index", "--", "/dev/null", file.Path], ct);
            return untracked.ExitCode is 0 or 1 ? DiffParser.Parse(untracked.StdOut) : throw new GitCommandException(untracked);
        }

        string[] args = staged
            ? ["diff", .. DiffOptions, "--find-renames", "--cached", "--", .. Paths(file)]
            : ["diff", .. DiffOptions, "--", file.Path];
        return DiffParser.Parse(await RunCheckedAsync(args, ct));
    }

    /// <summary>Префиксы a/ b/ явно: из этого diff строится патч для git apply, а diff.noprefix в конфиге их убирает.</summary>
    private static readonly string[] DiffOptions = ["--no-color", "--no-ext-diff", "--src-prefix=a/", "--dst-prefix=b/"];

    /// <summary>Аргументы применения патча к индексу: stage выбранных строк или, с <paramref name="reverse"/>, unstage.</summary>
    public static string[] ApplyToIndexArguments(bool reverse) =>
        reverse ? ["apply", "--cached", "--reverse", "--whitespace=nowarn", "-"] : ["apply", "--cached", "--whitespace=nowarn", "-"];

    /// <summary>Аргументы отката патча в рабочей копии: сброс выбранных строк. Индекс не меняется.</summary>
    public static readonly string[] DiscardLinesArguments = ["apply", "--reverse", "--whitespace=nowarn", "-"];

    /// <summary>
    /// Откатывает в рабочей копии строки из патча, построенного по diff «индекс → рабочая копия» с reverse.
    /// Необратимо: этих изменений больше нигде нет.
    /// </summary>
    public Task<GitResult> DiscardLinesAsync(string patch, CancellationToken ct = default) =>
        RunActionAsync(DiscardLinesArguments, ct, patch);

    /// <summary>Применяет патч из <see cref="PatchBuilder"/> к индексу. Рабочая копия не меняется.</summary>
    public Task<GitResult> ApplyToIndexAsync(string patch, bool reverse, CancellationToken ct = default) =>
        RunActionAsync(ApplyToIndexArguments(reverse), ct, patch);

    /// <summary>Сообщение и изменения коммита. Для первого коммита diff — относительно пустого дерева.</summary>
    public async Task<CommitDiff> GetCommitDiffAsync(Commit commit, CancellationToken ct = default) =>
        CommitDiffParser.Parse(await RunCheckedAsync(CommitDiffParser.Arguments(commit.Hash), ct));

    /// <summary>Переключается на ветку. Незакоммиченные изменения переносятся, если не конфликтуют.</summary>
    public Task<GitResult> SwitchAsync(Branch branch, CancellationToken ct = default) =>
        RunActionAsync(["switch", "--no-guess", branch.Name], ct);

    /// <summary>Аргументы создания ветки от коммита с переключением на неё.</summary>
    public static string[] CreateBranchAtArguments(string name, Commit commit) => ["switch", "--create", name, commit.Hash];

    /// <summary>Создаёт ветку от выбранного коммита и переключается на неё.</summary>
    public Task<GitResult> CreateBranchAtAsync(string name, Commit commit, CancellationToken ct = default) =>
        RunActionAsync(CreateBranchAtArguments(name, commit), ct);

    public static string[] CreateTagArguments(string name, Commit commit) => ["tag", name, commit.Hash];

    /// <summary>Ставит лёгкий (без сообщения) тег на коммит.</summary>
    public Task<GitResult> CreateTagAsync(string name, Commit commit, CancellationToken ct = default) =>
        RunActionAsync(CreateTagArguments(name, commit), ct);

    /// <summary>
    /// Аргументы checkout ссылки из графа: локальная ветка — переключение, удалённая — локальная ветка
    /// с тем же именем, следящая за ней, без ветки — detached HEAD на коммите.
    /// </summary>
    public static string[] CheckoutArguments(CommitRef? reference, Commit commit) => reference switch
    {
        { Kind: RefKind.Branch } => ["switch", "--no-guess", reference.Name],
        { Kind: RefKind.Remote } => ["switch", "--track", reference.Name],
        _ => ["switch", "--detach", commit.Hash],
    };

    public Task<GitResult> CheckoutAsync(CommitRef? reference, Commit commit, CancellationToken ct = default) =>
        RunActionAsync(CheckoutArguments(reference, commit), ct);

    /// <summary>Аргументы merge ветки или коммита (если на нём нет ветки) в текущую.</summary>
    public static string[] MergeRevisionArguments(string revision) => ["merge", "--no-edit", revision];

    public Task<GitResult> MergeRevisionAsync(string revision, CancellationToken ct = default) =>
        RunActionAsync(MergeRevisionArguments(revision), ct);

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

    /// <summary>Заголовки коммитов диапазона (например, origin/main..HEAD), старые первыми; неизвестный диапазон — пусто.</summary>
    public async Task<IReadOnlyList<string>> GetCommitSubjectsAsync(string range, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["log", "--reverse", "--format=%s", range, "--"], ct);
        return result.Success ? result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries) : [];
    }

    /// <summary>Основная ветка сервера по refs/remotes/origin/HEAD («main») без обращения к сети; null — неизвестна.</summary>
    public async Task<string?> GetRemoteDefaultBranchAsync(string remote = "origin", CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["symbolic-ref", "--quiet", "--short", $"refs/remotes/{remote}/HEAD"], ct);
        var name = result.StdOut.Trim();
        return result.Success && name.StartsWith(remote + "/") ? name[(remote.Length + 1)..] : null;
    }

    /// <summary>Адрес remote origin, а без него — первого remote; null, если remote нет.</summary>
    public async Task<string?> GetRemoteUrlAsync(CancellationToken ct = default)
    {
        var remotes = await GetRemotesAsync(ct);
        if (remotes.Count == 0)
            return null;

        var result = await _git.RunAsync(RepoRoot, ["remote", "get-url", remotes[0]], ct);
        return result.Success ? result.StdOut.Trim() : null;
    }

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

    public async Task<IReadOnlyList<StashEntry>> GetStashesAsync(CancellationToken ct = default) =>
        StashParser.Parse(await RunCheckedAsync(StashParser.Arguments, ct));

    /// <summary>Аргументы stash всех изменений, включая новые файлы. Пустое сообщение — git подставит «WIP on …».</summary>
    public static string[] StashArguments(string message) =>
        message.Length == 0
            ? ["stash", "push", "--include-untracked"]
            : ["stash", "push", "--include-untracked", "--message", message];

    public Task<GitResult> StashAsync(string message, CancellationToken ct = default) =>
        RunActionAsync(StashArguments(message), ct);

    /// <summary>Применяет stash и удаляет его. При конфликте git stash не удаляет.</summary>
    public Task<GitResult> StashPopAsync(StashEntry stash, CancellationToken ct = default) =>
        RunActionAsync(["stash", "pop", stash.Ref], ct);

    /// <summary>Применяет stash, оставляя его в списке.</summary>
    public Task<GitResult> StashApplyAsync(StashEntry stash, CancellationToken ct = default) =>
        RunActionAsync(["stash", "apply", stash.Ref], ct);

    public static string[] StashDropArguments(StashEntry stash) => ["stash", "drop", stash.Ref];

    /// <summary>Удаляет stash. Необратимо (кроме поиска потерянного коммита через git fsck).</summary>
    public Task<GitResult> StashDropAsync(StashEntry stash, CancellationToken ct = default) =>
        RunActionAsync(StashDropArguments(stash), ct);

    /// <summary>Изменения stash, включая сохранённые новые файлы.</summary>
    public async Task<CommitDiff> GetStashDiffAsync(StashEntry stash, CancellationToken ct = default)
    {
        var output = await RunCheckedAsync(
            ["stash", "show", "--patch", "--include-untracked", "--no-color", "--no-ext-diff", stash.Ref], ct);
        return new CommitDiff(stash.Message, DiffParser.Parse(output));
    }

    /// <summary>
    /// Незавершённая операция по служебным файлам в .git: MERGE_HEAD, CHERRY_PICK_HEAD, REVERT_HEAD, rebase-merge/.
    /// Папка .git определяется через git, поэтому работает и в worktree.
    /// </summary>
    public async Task<RepoOperation> GetOperationAsync(CancellationToken ct = default)
    {
        var gitDir = await GetGitDirAsync(ct);
        if (Directory.Exists(Path.Combine(gitDir, "rebase-merge")) || Directory.Exists(Path.Combine(gitDir, "rebase-apply")))
            return RepoOperation.Rebase;
        if (File.Exists(Path.Combine(gitDir, "MERGE_HEAD")))
            return RepoOperation.Merge;
        if (File.Exists(Path.Combine(gitDir, "CHERRY_PICK_HEAD")))
            return RepoOperation.CherryPick;
        if (File.Exists(Path.Combine(gitDir, "REVERT_HEAD")))
            return RepoOperation.Revert;
        return RepoOperation.None;
    }

    /// <summary>Аргументы прерывания операции: возвращают состояние до её начала.</summary>
    public static string[] AbortArguments(RepoOperation operation) => operation switch
    {
        RepoOperation.Merge => ["merge", "--abort"],
        RepoOperation.CherryPick => ["cherry-pick", "--abort"],
        RepoOperation.Revert => ["revert", "--abort"],
        RepoOperation.Rebase => ["rebase", "--abort"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Нечего прерывать"),
    };

    public Task<GitResult> AbortAsync(RepoOperation operation, CancellationToken ct = default) =>
        RunActionAsync(AbortArguments(operation), ct);

    /// <summary>Аргументы продолжения операции после решения конфликтов (или правки коммита при rebase).</summary>
    public static string[] ContinueArguments(RepoOperation operation) => operation switch
    {
        RepoOperation.Merge => ["merge", "--continue"],
        RepoOperation.CherryPick => ["cherry-pick", "--continue"],
        RepoOperation.Revert => ["revert", "--continue"],
        RepoOperation.Rebase => ["rebase", "--continue"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Нечего продолжать"),
    };

    /// <summary>Продолжает операцию. Сообщение коммита git берёт готовое: редактор не открывается.</summary>
    public Task<GitResult> ContinueAsync(RepoOperation operation, CancellationToken ct = default) =>
        RunActionAsync(ContinueArguments(operation), ct);

    /// <summary>
    /// Аргументы interactive rebase от <paramref name="oldest"/> (включительно) до HEAD.
    /// У первого коммита репозитория родителя нет — тогда --root. --autostash прячет и возвращает незакоммиченные изменения.
    /// </summary>
    public static string[] InteractiveRebaseArguments(Commit oldest) =>
        oldest.Parents.Count == 0
            ? ["rebase", "--interactive", "--autostash", "--root"]
            : ["rebase", "--interactive", "--autostash", oldest.Parents[0]];

    /// <summary>
    /// Выполняет interactive rebase по плану <paramref name="steps"/> (новые коммиты первыми, как в логе).
    /// Todo и сообщения для reword пишутся в .git/lazygit-cs-rebase/ и подставляются через GIT_SEQUENCE_EDITOR,
    /// поэтому редактор не открывается. При конфликте или edit git останавливается, операция остаётся незавершённой.
    /// </summary>
    public async Task<GitResult> InteractiveRebaseAsync(IReadOnlyList<RebaseStep> steps, CancellationToken ct = default)
    {
        var dir = Path.Combine(await GetGitDirAsync(ct), "lazygit-cs-rebase");
        // Файлы прошлого rebase больше не нужны: новый не начнётся, пока тот не закончен.
        if (Directory.Exists(dir))
            Directory.Delete(dir, recursive: true);
        Directory.CreateDirectory(dir);

        foreach (var step in steps.Where(s => s.Action == RebaseAction.Reword))
        {
            var body = await RunCheckedAsync(["log", "-1", "--format=%b", step.Commit.Hash], ct);
            await File.WriteAllTextAsync(MessageFile(step.Commit), RebaseTodo.RewordMessage(step.NewSubject ?? step.Commit.Subject, body), ct);
        }

        var todo = Path.Combine(dir, "todo");
        await File.WriteAllTextAsync(todo, RebaseTodo.Build(steps, MessageFile), ct);

        var environment = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = $"cp {RebaseTodo.ShellQuote(todo)}" };
        return await RunActionAsync(InteractiveRebaseArguments(RebaseTodo.Oldest(steps)), ct, environment: environment);

        string MessageFile(Commit commit) => Path.Combine(dir, $"message-{commit.Hash}");
    }

    private async Task<string> GetGitDirAsync(CancellationToken ct) =>
        _gitDir ??= (await RunCheckedAsync(["rev-parse", "--absolute-git-dir"], ct)).Trim();

    /// <summary>
    /// Решает конфликт, оставляя целиком нашу (<paramref name="ours"/>) или их версию файлов, и добавляет их в индекс.
    /// Вернуть конфликт: git checkout -m -- путь.
    /// </summary>
    public async Task<GitResult> ResolveConflictAsync(IReadOnlyList<FileChange> files, bool ours, CancellationToken ct = default)
    {
        var paths = files.Select(f => f.Path).ToList();
        var checkout = await RunActionAsync(["checkout", ours ? "--ours" : "--theirs", "--", .. paths], ct);
        return checkout.Success ? await RunActionAsync(["add", "--", .. paths], ct) : checkout;
    }

    /// <summary>
    /// Решает конфликт, оставляя обе версии: сначала наши строки, затем их (git merge-file --union), и добавляет в индекс.
    /// Версии берутся из индекса побайтово (checkout-index --temp), так что кодировка и переводы строк сохраняются.
    /// Если одна сторона файл удалила или файл бинарный, объединить нельзя — git вернёт ошибку.
    /// </summary>
    public async Task<GitResult> ResolveBothAsync(IReadOnlyList<FileChange> files, CancellationToken ct = default)
    {
        GitResult? last = null;
        foreach (var file in files)
        {
            var temps = new List<string>();
            try
            {
                var theirs = await ExtractStageAsync(file.Path, stage: 3, ct);
                if (theirs.Path is null)
                    return theirs.Result;
                temps.Add(theirs.Path);

                // Без общей базы (файл добавлен в обеих ветках) объединяем относительно пустого файла.
                var @base = await ExtractStageAsync(file.Path, stage: 1, ct);
                var basePath = @base.Path ?? CreateEmptyTemp();
                temps.Add(basePath);

                var ours = await RunActionAsync(["checkout", "--ours", "--", file.Path], ct);
                if (!ours.Success)
                    return ours;

                var union = await RunActionAsync(["merge-file", "--union", file.Path, basePath, theirs.Path], ct);
                if (!union.Success)
                    return union;

                last = await RunActionAsync(["add", "--", file.Path], ct);
                if (!last.Success)
                    return last;
            }
            finally
            {
                foreach (var temp in temps)
                    File.Delete(temp);
            }
        }

        return last ?? new GitResult("git merge-file --union", 0, "", "");
    }

    /// <summary>Выгружает версию файла из индекса (1 — база, 2 — наша, 3 — их) во временный файл в корне репозитория.</summary>
    private async Task<(string? Path, GitResult Result)> ExtractStageAsync(string path, int stage, CancellationToken ct)
    {
        var result = await _git.RunAsync(RepoRoot, ["checkout-index", $"--stage={stage}", "--temp", "--", path], ct);
        if (!result.Success)
            return (null, result);

        // Вывод: «.merge_file_AbC123\tпуть».
        var name = result.StdOut.Split('\t')[0].Trim();
        return (Path.Combine(RepoRoot, name), result);
    }

    private string CreateEmptyTemp()
    {
        var path = Path.Combine(RepoRoot, $".merge_file_empty_{Guid.NewGuid():N}");
        File.WriteAllBytes(path, []);
        return path;
    }

    /// <summary>Создаёт коммит из изменений в индексе. Сообщение передаётся через stdin.</summary>
    public Task<GitResult> CommitAsync(string message, CancellationToken ct = default) =>
        RunActionAsync(["commit", "--file=-", "--cleanup=strip"], ct, input: message);

    /// <summary>Аргументы amend: добавляет индекс в последний коммит, сообщение не меняется.</summary>
    public static readonly string[] AmendArguments = ["commit", "--amend", "--no-edit"];

    /// <summary>Добавляет staged-изменения в последний коммит. Переписывает историю ветки.</summary>
    public Task<GitResult> AmendAsync(CancellationToken ct = default) =>
        RunActionAsync(AmendArguments, ct);

    public static string[] ResetArguments(Commit commit, ResetMode mode) =>
        ["reset", $"--{mode.ToString().ToLowerInvariant()}", commit.Hash];

    /// <summary>Переносит текущую ветку на коммит. Hard теряет изменения рабочей копии. Переписывает историю.</summary>
    public Task<GitResult> ResetAsync(Commit commit, ResetMode mode, CancellationToken ct = default) =>
        RunActionAsync(ResetArguments(commit, mode), ct);

    /// <summary>Аргументы revert: новый коммит, отменяющий изменения выбранного. История не переписывается.</summary>
    public static string[] RevertArguments(Commit commit) => ["revert", "--no-edit", commit.Hash];

    public Task<GitResult> RevertAsync(Commit commit, CancellationToken ct = default) =>
        RunActionAsync(RevertArguments(commit), ct);

    public static string[] CherryPickArguments(string hash) => ["cherry-pick", hash];

    /// <summary>Копирует коммит в текущую ветку новым коммитом. При конфликте операция остаётся незавершённой.</summary>
    public Task<GitResult> CherryPickAsync(string hash, CancellationToken ct = default) =>
        RunActionAsync(CherryPickArguments(hash), ct);

    /// <summary>
    /// Находит коммит по хешу, сокращённому хешу или имени ветки/тега и возвращает его полный хеш,
    /// либо null. Ввод, начинающийся с «-», отвергается: git принял бы его за опцию.
    /// </summary>
    public async Task<Commit?> FindCommitAsync(string revision, CancellationToken ct = default)
    {
        if (revision.Length == 0 || revision.StartsWith('-'))
            return null;

        var result = await _git.RunAsync(RepoRoot,
            ["log", "-1", "-z", "--format=%H%x1f%an%x1f%ae%x1f%aI%x1f%s", revision + "^{commit}", "--"], ct);
        return result.Success ? Parsers.LogParser.Parse(result.StdOut).FirstOrDefault() : null;
    }

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
    private async Task<GitResult> RunActionAsync(IReadOnlyList<string> args, CancellationToken ct, string? input = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var started = DateTimeOffset.Now;
        try
        {
            var result = await _git.RunAsync(RepoRoot, args, ct, input, environment);
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
