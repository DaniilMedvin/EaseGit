using System.Text;
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

    /// <summary>Коммиты, менявшие файл, новые первыми; переименования прослеживаются (--follow).</summary>
    public async Task<IReadOnlyList<FileRevision>> GetFileHistoryAsync(string path, int limit = GraphLogLimit, CancellationToken ct = default) =>
        FileHistoryParser.Parse(await RunCheckedAsync(FileHistoryParser.Arguments(path, limit), ct));

    /// <summary>Сообщение коммита и diff только файла из <paramref name="revision"/> (с прежним именем у переименования).</summary>
    public async Task<CommitDiff> GetFileRevisionDiffAsync(FileRevision revision, CancellationToken ct = default) =>
        CommitDiffParser.Parse(await RunCheckedAsync(
            CommitDiffParser.Arguments(revision.Commit.Hash, revision.OldPath is null ? [revision.Path] : [revision.OldPath, revision.Path]), ct));

    /// <summary>
    /// Кто и в каком коммите написал каждую строку. Без <paramref name="revision"/> — рабочая копия:
    /// незакоммиченные строки с нулевым хешем. Переименования git прослеживает сам.
    /// </summary>
    public async Task<IReadOnlyList<BlameLine>> GetBlameAsync(string path, string? revision = null, CancellationToken ct = default) =>
        BlameParser.Parse(await RunCheckedAsync(BlameParser.Arguments(path, revision), ct));

    /// <summary>Изменённые файлы между <paramref name="left"/> и <paramref name="right"/> со счётчиками строк.</summary>
    /// <param name="fromMergeBase">От общего предка (что принесла <paramref name="right"/>) или между вершинами.</param>
    public async Task<IReadOnlyList<ComparedFile>> CompareFilesAsync(string left, string right, bool fromMergeBase, CancellationToken ct = default)
    {
        var range = CompareParser.Range(left, right, fromMergeBase);
        var nameStatus = RunCheckedAsync(CompareParser.NameStatusArguments(range), ct);
        var numstat = RunCheckedAsync(CompareParser.NumstatArguments(range), ct);
        return CompareParser.Parse(await nameStatus, await numstat);
    }

    /// <summary>Diff одного файла сравнения (с прежним именем у переименования).</summary>
    public async Task<IReadOnlyList<DiffLine>> CompareFileDiffAsync(string left, string right, bool fromMergeBase, ComparedFile file,
        CancellationToken ct = default) =>
        DiffParser.Parse(await RunCheckedAsync(
            ["diff", .. DiffOptions, "--find-renames", CompareParser.Range(left, right, fromMergeBase), "--",
             .. file.OldPath is null ? (string[])[file.Path] : [file.OldPath, file.Path]], ct));

    /// <summary>Коммиты, которые есть в <paramref name="right"/> и нет в <paramref name="left"/>, новые первыми.</summary>
    public async Task<IReadOnlyList<Commit>> CommitsOnlyInAsync(string left, string right, CancellationToken ct = default) =>
        LogParser.Parse(await RunCheckedAsync(LogParser.Arguments(GraphLogLimit, range: $"{left}..{right}"), ct));

    /// <summary>Сколько коммитов только в <paramref name="left"/> и только в <paramref name="right"/>.</summary>
    public async Task<(int OnlyLeft, int OnlyRight)> CountDivergenceAsync(string left, string right, CancellationToken ct = default)
    {
        var parts = (await RunCheckedAsync(["rev-list", "--left-right", "--count", $"{left}...{right}", "--"], ct))
            .Split((char[])['\t', ' '], StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out var l) && int.TryParse(parts[1], out var r) ? (l, r) : (0, 0);
    }

    /// <summary>Общий предок двух веток или коммитов; null — общей истории нет.</summary>
    public async Task<string?> GetMergeBaseAsync(string left, string right, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["merge-base", left, right], ct);
        return result.Success ? result.StdOut.Trim() : null;
    }

    /// <summary>Отбор коммитов для поиска: текст ищется как есть (точки и скобки — обычные символы), без учёта регистра.</summary>
    public static string[] SearchFilters(string query, HistorySearchMode mode) => mode switch
    {
        HistorySearchMode.Message => ["--regexp-ignore-case", "--fixed-strings", $"--grep={query}"],
        HistorySearchMode.Author => ["--regexp-ignore-case", "--fixed-strings", $"--author={query}"],
        // -S — строка как есть: коммиты, где число её вхождений в коде изменилось (появилась или исчезла).
        _ => [$"-S{query}"],
    };

    /// <summary>Коммиты текущей ветки (или всех, с <paramref name="allBranches"/>), подходящие под запрос, новые первыми.</summary>
    public async Task<IReadOnlyList<Commit>> SearchLogAsync(string query, HistorySearchMode mode, bool allBranches,
        CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, LogParser.Arguments(GraphLogLimit, allBranches, filters: SearchFilters(query, mode)), ct);
        if (result.Success)
            return LogParser.Parse(result.StdOut);
        return await HasCommitsAsync(ct) ? throw new GitCommandException(result) : [];
    }

    /// <summary>Пустое дерево git: «родитель» первого коммита для сравнения.</summary>
    public const string EmptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904";

    /// <summary>Текст файла в коммите (<c>rev:path</c>) или в индексе (<paramref name="revision"/> = ""); нет такого — null.</summary>
    public async Task<string?> GetFileTextAsync(string revision, string path, CancellationToken ct = default)
    {
        var result = await _git.RunAsync(RepoRoot, ["show", $"{revision}:{path}"], ct);
        return result.Success ? result.StdOut : null;
    }

    /// <summary>
    /// Изменения кода в C#-файле рабочей копии: «Изменения» — индекс против диска, Staged — HEAD против индекса,
    /// новый — пусто против диска. Не C# — пусто.
    /// </summary>
    public async Task<IReadOnlyList<CodeChange>> GetCodeChangesAsync(FileChange file, bool staged, CancellationToken ct = default)
    {
        if (!Highlighting.DiffHighlighter.IsCSharp(file.Path))
            return [];

        string? before, after;
        if (staged)
        {
            before = await GetFileTextAsync("HEAD", file.OriginalPath ?? file.Path, ct);
            after = await GetFileTextAsync("", file.Path, ct);
        }
        else
        {
            before = file.IsUntracked ? null : await GetFileTextAsync("", file.Path, ct);
            var full = Path.Combine(RepoRoot, file.Path);
            after = File.Exists(full) ? await File.ReadAllTextAsync(full, ct) : null;
        }

        return Highlighting.CodeChanges.Compare(before, after);
    }

    /// <summary>
    /// Изменения кода во всех C#-файлах между двумя ревизиями (для коммита — родитель и коммит, у первого — пустое дерево),
    /// по файлам; файлы без изменений кода (только форматирование) пропускаются.
    /// </summary>
    public async Task<IReadOnlyList<(string Path, IReadOnlyList<CodeChange> Changes)>> GetCodeChangesAsync(string from, string to,
        CancellationToken ct = default)
    {
        var files = CompareParser.Parse(
            await RunCheckedAsync(["diff", "--name-status", "-z", "--find-renames", from, to, "--"], ct), "");
        var result = new List<(string, IReadOnlyList<CodeChange>)>();
        foreach (var file in files.Where(f => Highlighting.DiffHighlighter.IsCSharp(f.Path)))
        {
            var before = file.Status == 'A' ? null : await GetFileTextAsync(from, file.OldPath ?? file.Path, ct);
            var after = file.Status == 'D' ? null : await GetFileTextAsync(to, file.Path, ct);
            var changes = Highlighting.CodeChanges.Compare(before, after);
            if (changes.Count > 0)
                result.Add((file.Path, changes));
        }

        return result;
    }

    /// <summary>Изменения кода в staged C#-файлах (HEAD против индекса) — для подсказки сообщения коммита.</summary>
    public async Task<IReadOnlyList<(string Path, IReadOnlyList<CodeChange> Changes)>> GetStagedCodeChangesAsync(CancellationToken ct = default)
    {
        var hasHead = await HasCommitsAsync(ct);
        var files = CompareParser.Parse(
            await RunCheckedAsync(["diff", "--cached", "--name-status", "-z", "--find-renames", "--"], ct), "");
        var result = new List<(string, IReadOnlyList<CodeChange>)>();
        foreach (var file in files.Where(f => Highlighting.DiffHighlighter.IsCSharp(f.Path)))
        {
            var before = file.Status == 'A' || !hasHead ? null : await GetFileTextAsync("HEAD", file.OldPath ?? file.Path, ct);
            var after = file.Status == 'D' ? null : await GetFileTextAsync("", file.Path, ct);
            var changes = Highlighting.CodeChanges.Compare(before, after);
            if (changes.Count > 0)
                result.Add((file.Path, changes));
        }

        return result;
    }

    /// <summary>Файлы репозитория в HEAD и индексе (для выбора файла, чью историю смотреть).</summary>
    public async Task<IReadOnlyList<string>> GetTrackedFilesAsync(CancellationToken ct = default) =>
        (await RunCheckedAsync(["ls-files", "-z"], ct)).Split('\0', StringSplitOptions.RemoveEmptyEntries);

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

    public static string[] RenameBranchArguments(Branch branch, string name) => ["branch", "--move", branch.Name, name];

    /// <summary>Переименовывает локальную ветку. Ветку на сервере git не трогает: её upstream остаётся прежним.</summary>
    public Task<GitResult> RenameBranchAsync(Branch branch, string name, CancellationToken ct = default) =>
        RunActionAsync(RenameBranchArguments(branch, name), ct);

    public async Task<IReadOnlyList<Tag>> GetTagsAsync(CancellationToken ct = default) =>
        TagParser.Parse(await RunCheckedAsync(TagParser.Arguments, ct));

    /// <summary>Аргументы аннотированного тега на HEAD (с сообщением) или лёгкого, если сообщение пустое.</summary>
    public static string[] CreateTagOnHeadArguments(string name, string message) =>
        message.Length == 0 ? ["tag", name] : ["tag", "--annotate", name, "--message", message];

    public Task<GitResult> CreateTagOnHeadAsync(string name, string message, CancellationToken ct = default) =>
        RunActionAsync(CreateTagOnHeadArguments(name, message), ct);

    public static string[] PushTagArguments(string remote, Tag tag) => ["push", remote, $"refs/tags/{tag.Name}"];

    public Task<GitResult> PushTagAsync(string remote, Tag tag, CancellationToken ct = default) =>
        RunActionAsync(PushTagArguments(remote, tag), ct);

    public static string[] DeleteTagArguments(Tag tag) => ["tag", "--delete", tag.Name];

    public static string[] DeleteRemoteTagArguments(string remote, Tag tag) => ["push", remote, "--delete", $"refs/tags/{tag.Name}"];

    /// <summary>Удаляет тег локально и, с <paramref name="remote"/>, на сервере (сначала на сервере: если там не вышло, локальный цел).</summary>
    public async Task<GitResult> DeleteTagAsync(Tag tag, string? remote, CancellationToken ct = default)
    {
        if (remote is not null)
        {
            var remoteResult = await RunActionAsync(DeleteRemoteTagArguments(remote, tag), ct);
            if (!remoteResult.Success)
                return remoteResult;
        }

        return await RunActionAsync(DeleteTagArguments(tag), ct);
    }

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

    /// <summary>Аргументы stash только этих файлов (вместе с новыми среди них); остальные изменения остаются.</summary>
    public static string[] StashPathsArguments(string message, IReadOnlyList<string> paths) =>
        [.. StashArguments(message), "--", .. paths];

    public Task<GitResult> StashPathsAsync(string message, IReadOnlyList<string> paths, CancellationToken ct = default) =>
        RunActionAsync(StashPathsArguments(message, paths), ct);

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

    /// <summary>Аргументы пропуска текущего коммита операции: его изменения выбрасываются, операция идёт дальше.</summary>
    public static string[] SkipArguments(RepoOperation operation) => operation switch
    {
        RepoOperation.CherryPick => ["cherry-pick", "--skip"],
        RepoOperation.Revert => ["revert", "--skip"],
        RepoOperation.Rebase => ["rebase", "--skip"],
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Нечего пропускать"),
    };

    public Task<GitResult> SkipAsync(RepoOperation operation, CancellationToken ct = default) =>
        RunActionAsync(SkipArguments(operation), ct);

    /// <summary>Продолжает операцию. Сообщение коммита git берёт готовое: редактор не открывается.</summary>
    public Task<GitResult> ContinueAsync(RepoOperation operation, CancellationToken ct = default) =>
        RunActionAsync(ContinueArguments(operation), ct);

    /// <summary>Аргументы первого шага fixup: временный коммит «fixup! …» из индекса.</summary>
    public static string[] FixupCommitArguments(Commit target) => ["commit", $"--fixup={target.Hash}"];

    /// <summary>Аргументы второго шага: rebase с --autosquash склеивает fixup с целью; редактор не открывается.</summary>
    public static string[] AutosquashArguments(Commit target) =>
        target.Parents.Count == 0
            ? ["rebase", "--interactive", "--autosquash", "--autostash", "--root"]
            : ["rebase", "--interactive", "--autosquash", "--autostash", target.Parents[0]];

    /// <summary>
    /// Встраивает staged-изменения в старый коммит <paramref name="target"/>: commit --fixup, затем rebase --autosquash.
    /// Коммиты после цели пересоздаются. При конфликте rebase останавливается, как обычно (C — продолжить, A — прервать).
    /// </summary>
    public async Task<GitResult> FixupAsync(Commit target, CancellationToken ct = default)
    {
        var commit = await RunActionAsync(FixupCommitArguments(target), ct);
        if (!commit.Success)
            return commit;

        // Список шагов autosquash уже правильный — подтверждаем его без изменений.
        var environment = new Dictionary<string, string> { ["GIT_SEQUENCE_EDITOR"] = "true" };
        return await RunActionAsync(AutosquashArguments(target), ct, environment: environment);
    }

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

    /// <summary>
    /// Читает файл с конфликтом для решения по блокам. Возвращает файл или текст, почему по блокам нельзя:
    /// не UTF-8 (запись могла бы испортить текст), нет маркеров (удалён в одной из веток, бинарный), маркеры не сходятся.
    /// </summary>
    public async Task<(ConflictFile? File, string? Problem)> ReadConflictFileAsync(string path, CancellationToken ct = default)
    {
        var full = Path.Combine(RepoRoot, path);
        if (!File.Exists(full))
            return (null, T("Файла нет в рабочей копии: он удалён в одной из веток. Реши целиком: o / t",
                "The file is missing in the working tree: it was deleted in one branch. Resolve it whole: o / t"));

        var bytes = await File.ReadAllBytesAsync(full, ct);
        var hasBom = bytes is [0xEF, 0xBB, 0xBF, ..];
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
        }
        catch (DecoderFallbackException)
        {
            return (null, T("Файл не в UTF-8 (или бинарный): по блокам нельзя, реши целиком: o / t",
                "The file is not UTF-8 (or binary): resolve it whole: o / t"));
        }

        var parts = ConflictResolver.Parse(text);
        if (parts is null)
            return (null, T("Маркеры конфликта в файле не сходятся: реши его в редакторе или целиком (o / t)",
                "Conflict markers in the file don't match: resolve it in an editor or whole (o / t)"));
        if (ConflictResolver.CountBlocks(parts) == 0)
            return (null, T("В файле нет блоков <<<<<<<: реши его целиком (o / t) или добавь в индекс (space)",
                "The file has no <<<<<<< blocks: resolve it whole (o / t) or stage it (space)"));
        return (new ConflictFile(path, parts, hasBom), null);
    }

    /// <summary>Записывает файл из частей (решённые блоки — выбранными строками) в той же кодировке.</summary>
    public async Task WriteConflictFileAsync(ConflictFile file, IReadOnlyList<ConflictPart> parts, CancellationToken ct = default)
    {
        var text = ConflictResolver.Render(parts);
        await File.WriteAllBytesAsync(Path.Combine(RepoRoot, file.Path),
            [.. file.HasBom ? (byte[])[0xEF, 0xBB, 0xBF] : [], .. new UTF8Encoding(false).GetBytes(text)], ct);
    }

    /// <summary>Аргументы возврата конфликта: файл снова с маркерами, как сразу после merge. Ручные правки пропадут.</summary>
    public static string[] RestoreConflictArguments(string path) => ["checkout", "--merge", "--", path];

    public Task<GitResult> RestoreConflictAsync(string path, CancellationToken ct = default) =>
        RunActionAsync(RestoreConflictArguments(path), ct);

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
