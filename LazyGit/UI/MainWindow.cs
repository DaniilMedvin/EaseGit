using LazyGit.Auth;
using LazyGit.Commands;
using LazyGit.Config;
using LazyGit.DotNet;
using LazyGit.Git;
using LazyGit.GitHub;
using LazyGit.Highlighting;
using LazyGit.Localization;
using LazyGit.Models;
using LazyGit.Templates;
using LazyGit.UI.Dialogs;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using CheckState = LazyGit.Models.CheckState;

namespace LazyGit.UI;

public enum MainResult
{
    Quit,
    Login,

    /// <summary>Тема или язык сменились: окно открывается заново с той же страницей репозитория.</summary>
    Reload,

    /// <summary>Открыть другой репозиторий: путь в <see cref="MainWindow.OpenRepoPath"/> (переход из PR в окне GitHub).</summary>
    OpenRepo,
}

/// <summary>
/// Главный экран репозитория: шапка, слева панели файлов, веток и коммитов, справа diff.
/// 1 / 2 / 3 и tab — переход между панелями.
/// В файлах: space — stage/unstage файла или папки, a — всех файлов, d — сбросить изменения.
/// В ветках: space — checkout, n — новая ветка, M — merge в текущую, D — удалить.
/// На конфликтном файле: o — оставить нашу версию, t — их, b — обе. A — прервать merge / cherry-pick / rebase, C — продолжить.
/// В коммитах: i — interactive rebase от выбранного коммита до HEAD.
/// s — спрятать изменения в stash; в панели stash: space — pop, a — apply, d — удалить.
/// ~ — Command Log, «,» — настройки, c — коммит, f — fetch, p — pull, P — push (во время операции esc отменяет её), r — обновить, ? — справка.
/// Экран обновляется автоматически при изменении файлов и состояния git.
/// Result: Quit (q), Login (ctrl+g — вход в GitHub) или null (Esc — назад к стартовому экрану).
/// </summary>
public sealed class MainWindow : Runnable<MainResult?>
{
    private readonly GitService _git;
    private readonly CommandRegistry _commands = new();
    private readonly ConfigStore _config;
    private readonly AuthStatus _auth;
    private readonly RepoHeader _header;
    private readonly FilesPanel _files;
    private readonly BranchesPanel _branches;
    private readonly CommitsPanel _commits;
    private readonly StashPanel _stash;
    private readonly Label _stashTitle;
    private readonly DiffPanel _diff;

    // git log --graph выбранной ветки на месте diff, пока в фокусе панель веток.
    private readonly GraphLogPanel _branchLog;

    // Ветка, лог которой показан справа: при обновлении той же ветки прокрутка сохраняется.
    private string? _branchLogFor;
    private readonly Label _filesTitle;
    private readonly Label _branchesTitle;
    private readonly Label _commitsTitle;
    private readonly CommandLogPanel _log;
    private readonly CommandLog _commandLog;
    private readonly Label _logTitle;
    private readonly Label _diffTitle;
    private readonly Label _status;
    private readonly HintBar _hints;
    private bool _busy;
    private object? _statusTimeout;
    private object? _spinnerTimeout;
    private CancellationTokenSource? _operation;

    // Ветка, для которой показано «Нажми P ещё раз, чтобы выполнить push -u»: повторный P публикует её.
    private string? _pushUpstreamOffer;
    private CancellationTokenSource? _diffLoading;
    private RepoWatcher? _watcher;
    private bool _refreshPending;
    private RepoStatus? _repoStatus;
    private RepoOperation _repoOperation;

    // Что собирать по F5 (.sln / .csproj) — ищется при обновлении экрана, а не на каждую проверку доступности команды.
    private string? _dotnetTarget;

    // Ветки, влитые копиями (rebase / squash), и кэш проверок: ключ — ветка, её вершина и вершина базы.
    private IReadOnlyDictionary<string, MergedCopy> _mergedCopies = new Dictionary<string, MergedCopy>();
    private readonly Dictionary<string, MergedCopy?> _mergedCache = [];
    private bool _mergedLoading;

    // Исключённые в окне проверки тесты: выбор живёт, пока открыт репозиторий, и не теряется между F5.
    private readonly HashSet<string> _excludedTests = new(StringComparer.Ordinal);

    // «Подробнее» (ctrl+e): над diff файла, коммита и stash — какие классы и методы C# изменились. По умолчанию выключено.
    private bool _codeDetails;

    // Сколько коммитов грузить в панель коммитов: растёт на LogPage, когда выбор доходит до конца списка.
    private int _logLimit = GitService.DefaultLogLimit;
    private const int LogPage = GitService.DefaultLogLimit;

    // Адрес remote и репозиторий на GitHub — перечитываются с остальным состоянием, чтобы окно GitHub открывалось сразу.
    private string? _remoteUrl;
    private GitHubRepo? _gitHubRepo;

    // Статус CI коммита HEAD на GitHub. Сеть не дёргается на каждое изменение файла: новый коммит — сразу,
    // пока CI идёт — раз в 15 с (и сам, по таймеру), иначе — не чаще раза в 2 минуты.
    private static readonly TimeSpan CiPendingInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan CiIdleInterval = TimeSpan.FromMinutes(2);
    private string? _ciSha;
    private DateTimeOffset _ciCheckedAt;
    private IReadOnlyList<WorkflowRun> _ciRuns = [];
    private bool _ciLoading;
    private object? _ciTimer;

    // Что сейчас показано справа (файл или коммит): при обновлении того же самого прокрутка сохраняется.
    private object? _diffKey;

    // Файл в режиме решения конфликта по блокам: он справа вместо diff, панель в фокусе. null — режим выключен.
    private ConflictFile? _conflict;
    private readonly ConflictPanel _conflictPanel;

    // Файл в режиме построчного stage: его diff справа, панель diff в фокусе. null — режим выключен.
    private StagingTarget? _staging;

    /// <param name="Staged">Unstage из staged-изменений, иначе stage из изменений рабочей копии.</param>
    private sealed record StagingTarget(string Path, bool Staged);

    private const int LogHeight = 8;

    private readonly string? _notice;

    // Проблемы пользовательской настройки (опечатка в теме или клавише): показываются при открытии.
    private readonly List<string> _configProblems = [];

    /// <param name="log">Журнал, в который <paramref name="git"/> пишет команды; показывается панелью по ~.</param>
    /// <param name="notice">Сообщение при открытии, например «Тема: light» после переключения.</param>
    public MainWindow(GitService git, AuthStatus auth, ConfigStore config, CommandLog log, string? notice = null)
    {
        _notice = notice;
        _git = git;
        _commandLog = log;
        _config = config;
        _auth = auth;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _header = new RepoHeader(git.RepoRoot, auth) { X = 0, Y = 0 };

        _files = new FilesPanel
        {
            X = 0,
            Y = Pos.Bottom(_header),
            Width = Dim.Percent(35),
            Height = Dim.Percent(45),
        };

        _branches = new BranchesPanel
        {
            X = 0,
            Y = Pos.Bottom(_files),
            Width = Dim.Width(_files),
            Height = Dim.Percent(20),
        };

        _commits = new CommitsPanel
        {
            X = 0,
            Y = Pos.Bottom(_branches),
            Width = Dim.Width(_files),
            Height = Dim.Fill(2),
        };

        // Stash виден, только когда не пуст: на маленьком экране место нужнее коммитам.
        _stash = new StashPanel
        {
            X = 0,
            Y = Pos.Bottom(_commits),
            Width = Dim.Width(_files),
            Height = 0,
            Visible = false,
        };
        _stash.SelectionChanged += (_, _) =>
        {
            UpdateTitles();
            if (_stash.HasFocus)
                _ = LoadDiffAsync();
        };
        // Справа — контекст панели в фокусе, поэтому выбор в остальных панелях его не меняет.
        _files.SelectionChanged += (_, _) =>
        {
            if (FocusedScope != CommandScope.Files)
                return;

            _ = LoadDiffAsync();
            // Подсказки o / t видны только на конфликтном файле.
            UpdateHints();
        };
        _branches.SelectionChanged += (_, _) =>
        {
            UpdateTitles();
            if (_branches.HasFocus)
                _ = LoadDiffAsync();
        };
        _commits.SelectionChanged += (_, _) =>
        {
            UpdateTitles();
            LoadMoreCommitsIfAtEnd();
            if (_commits.HasFocus)
                _ = LoadDiffAsync();
        };

        _diff = new DiffPanel
        {
            X = Pos.Right(_files),
            Y = Pos.Top(_files),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
        };
        _diffTitle = BoxTitle.For(_diff, "Diff", Theme.Muted);

        _conflictPanel = new ConflictPanel
        {
            X = Pos.Right(_files),
            Y = Pos.Top(_files),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Visible = false,
        };
        // Ушли из панели конфликта (esc, tab, 1–4, мышь) — режим заканчивается, справа снова diff.
        _conflictPanel.HasFocusChanged += (_, _) =>
        {
            if (!_conflictPanel.HasFocus)
                StopConflict();
        };

        _branchLog = new GraphLogPanel
        {
            X = Pos.Right(_files),
            Y = Pos.Top(_files),
            Width = Dim.Fill(),
            Height = Dim.Fill(2),
            Visible = false,
        };
        _diff.Mascot = config.Load().Mascot;
        _diff.Wrap = config.Load().WrapDiff ?? true;

        _log = new CommandLogPanel
        {
            X = Pos.Right(_files),
            Y = Pos.Bottom(_diff),
            Width = Dim.Fill(),
            Height = LogHeight,
        };
        _logTitle = BoxTitle.For(_log, "Command Log", Theme.Muted);
        _log.SetEntries(log.Entries);
        log.Changed += (_, _) => App?.Invoke(() => _log.SetEntries(log.Entries));
        SetLogVisible(config.Load().ShowCommandLog);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        _hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };

        Add(
            _header, BoxTitle.For(_header, "LGit", Theme.Accent),
            _files, _filesTitle = BoxTitle.ForFocusable(_files, T("[1] Файлы", "[1] Files")),
            _branches, _branchesTitle = BoxTitle.ForFocusable(_branches, T("[2] Ветки", "[2] Branches")),
            _commits, _commitsTitle = BoxTitle.ForFocusable(_commits, T("[3] Коммиты", "[3] Commits")),
            _stash, _stashTitle = BoxTitle.ForFocusable(_stash, "[4] Stash"),
            _diff, _branchLog, _conflictPanel, _diffTitle,
            _log, _logTitle,
            _status, _hints);
        // Справа — контекст панели в фокусе: diff файла, ветка или содержимое коммита.
        _files.HasFocusChanged += (_, _) => OnPanelFocusChanged(_files);
        _branches.HasFocusChanged += (_, _) => OnPanelFocusChanged(_branches);
        _commits.HasFocusChanged += (_, _) => OnPanelFocusChanged(_commits);
        _stash.HasFocusChanged += (_, _) => OnPanelFocusChanged(_stash);
        // Ушли из diff (esc, tab, 1–4, мышь) — режим построчного stage заканчивается.
        _diff.HasFocusChanged += (_, _) =>
        {
            if (!_diff.HasFocus)
                StopLineStaging();
        };
        _stashTitle.Visible = false;
        RegisterCommands();
        _files.SetFocus();
        UpdateHints();
    }

    private void OnPanelFocusChanged(View panel)
    {
        // Панель может получить фокус раньше, чем diff узнает, что потерял его: режим выключается до загрузки её diff.
        if (panel.HasFocus)
            StopLineStaging();
        UpdateHints();
        if (panel.HasFocus)
            _ = LoadDiffAsync();
    }

    /// <summary>Подсказки внизу — из команд панели в фокусе и глобальных.</summary>
    private void UpdateHints() =>
        _hints.SetHints(_commands.Hints(FocusedScope).Select(c => new Hint(c.Key.Label, c.Hint!, c.HintOptional)));

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            _ = RefreshAsync();
            StartWatching();
            ShowStartupMessage();
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
            if (_ciTimer is not null)
                App?.RemoveTimeout(_ciTimer);
            _ciTimer = null;
        }
    }

    /// <summary>Сообщает о смене темы или о проблемах в config.json; по ним же ничего не ломается, берутся значения по умолчанию.</summary>
    private void ShowStartupMessage()
    {
        if (_configProblems.Count > 0)
        {
            var more = _configProblems.Count > 1 ? T($" (и ещё {_configProblems.Count - 1})", $" (and {_configProblems.Count - 1} more)") : "";
            ShowStatus($"{Theme.SymbolWarning} config.json: {_configProblems[0]}{more}", Theme.Warning, autoHide: true);
        }
        else if (_notice is not null)
        {
            ShowStatus($"{Theme.SymbolSuccess} {_notice}", Theme.Success, autoHide: true);
        }
    }

    private void StartWatching()
    {
        try
        {
            _watcher = new RepoWatcher(_git.RepoRoot);
            _watcher.Changed += (_, _) => App?.Invoke(() => _ = RefreshAsync(silent: true));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or PlatformNotSupportedException)
        {
            // Без автообновления экран по-прежнему обновляется после действий и по r.
            ShowStatus(T($"{Theme.SymbolWarning} Автообновление недоступно: {ex.Message}", $"{Theme.SymbolWarning} Auto-refresh unavailable: {ex.Message}"), Theme.Warning);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        // Во время push/pull/fetch esc отменяет операцию, а не уходит со страницы.
        if (key == Key.Esc && _operation is { } operation)
        {
            operation.Cancel();
            return true;
        }

        if (_commands.Find(FocusedScope, key) is { } command)
        {
            command.Execute();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private CommandScope FocusedScope =>
        _conflict is not null && _conflictPanel.HasFocus ? CommandScope.Conflict
        : _staging is not null && _diff.HasFocus ? CommandScope.Diff
        : _branches.HasFocus ? CommandScope.Branches
        : _commits.HasFocus ? CommandScope.Commits
        : _stash.HasFocus ? CommandScope.Stash
        : CommandScope.Files;

    /// <summary>
    /// Все команды экрана. Порядок — порядок в подсказках внизу и в справке.
    /// Клавиши панели проверяются раньше глобальных, поэтому space в файлах — stage, а в ветках — checkout.
    /// </summary>
    private void RegisterCommands()
    {
        var config = _config.Load();
        var keyOverrides = config.Keys ?? [];
        var overridden = new HashSet<string>();
        if (ThemeCatalog.Resolve(config).Problem is { } themeProblem)
            _configProblems.Add(themeProblem);

        void Add(string id, string title, CommandScope scope, CommandKey shortcut, Action execute, string? hint = null,
            bool hintOptional = false, Func<string?>? preview = null, bool dangerous = false, bool hidden = false,
            Func<bool>? available = null, string keywords = "", string? unavailableNote = null)
        {
            if (keyOverrides.TryGetValue(id, out var text))
            {
                var (custom, error) = KeyParser.Parse(text);
                if (custom is null)
                    _configProblems.Add(T($"клавиша команды {id}: {error}", $"key for command {id}: {error}"));
                else
                {
                    shortcut = custom;
                    overridden.Add(id);
                }
            }

            _commands.Add(new AppCommand
            {
                Id = id,
                Title = title,
                Scope = scope,
                Key = shortcut,
                Execute = execute,
                Hint = hint,
                HintOptional = hintOptional,
                Preview = preview,
                IsDangerous = dangerous,
                HiddenInPalette = hidden,
                IsAvailable = available ?? (() => true),
                Keywords = keywords,
                UnavailableNote = unavailableNote,
            });
        }

        Add("files.stage", "Stage / unstage", CommandScope.Files, CommandKey.Of("space", Key.Space),
            () => _ = ToggleSelectedAsync(), "stage", preview: StagePreview, keywords: "добавить в индекс стейдж");
        Add("files.stageAll", T("Stage / unstage всех", "Stage / unstage all"), CommandScope.Files, CommandKey.Of("a", Key.A),
            () => _ = ToggleAllAsync(), T("все", "all"), hintOptional: true,
            preview: () => _files.Entries.Any(r => r.Section != FileSection.Staged) ? "git add --all" : "git reset --quiet", keywords: "добавить всё индекс");
        Add("files.ours", T("Оставить нашу версию", "Keep our version"), CommandScope.Files, CommandKey.Of("o", Key.O),
            () => _ = ResolveConflictAsync(ours: true), T("наша", "ours"), available: IsConflictSelected,
            preview: () => ConflictPreview("--ours"), keywords: "конфликт ours наш мой");
        Add("files.theirs", T("Оставить их версию", "Keep their version"), CommandScope.Files, CommandKey.Of("t", Key.T),
            () => _ = ResolveConflictAsync(ours: false), T("их", "theirs"), available: IsConflictSelected,
            preview: () => ConflictPreview("--theirs"), keywords: "конфликт theirs их чужой");
        Add("files.both", T("Оставить обе версии", "Keep both versions"), CommandScope.Files, CommandKey.Of("b", Key.B),
            () => _ = ResolveBothAsync(), T("оба", "both"), available: IsConflictSelected,
            preview: () => SelectedConflictFiles() is { Count: > 0 } f
                ? CommandText.Format(["merge-file", "--union", .. f.Select(x => x.Path)]) + " && git add …"
                : null,
            keywords: "конфликт оба обе union объединить вместе");
        Add("files.discard", T("Сбросить изменения", "Discard changes"), CommandScope.Files, CommandKey.Of("d", Key.D),
            () => _ = DiscardSelectedAsync(), T("сбросить", "discard"), dangerous: true, keywords: "откатить отменить удалить ресет");
        Add("files.stageLines", T("Построчный stage файла", "Stage file by lines"), CommandScope.Files, CommandKey.Of("enter", Key.Enter),
            StartLineStaging, T("по строкам", "by lines"), available: () => _files.SelectedFile is { Section: not FileSection.Conflicts },
            keywords: "строки строка hunk ханк фрагмент частично патч patch");

        Add("files.resolveBlocks", T("Решить конфликт по блокам", "Resolve the conflict block by block"), CommandScope.Files,
            CommandKey.Of("enter", Key.Enter), () => _ = StartConflictAsync(), T("по блокам", "by blocks"),
            available: () => _files.SelectedRow is FileEntryRow { Section: FileSection.Conflicts },
            keywords: "конфликт блок фрагмент hunk ours theirs наша их merge решить");
        Add("conflict.ours", T("Оставить нашу версию блока", "Keep our version of the block"), CommandScope.Conflict, CommandKey.Of("o", Key.O),
            () => _ = ResolveBlockAsync(ConflictChoice.Ours), T("наша", "ours"), keywords: "конфликт блок ours наш", available: () => _conflict is not null);
        Add("conflict.theirs", T("Оставить их версию блока", "Keep their version of the block"), CommandScope.Conflict, CommandKey.Of("t", Key.T),
            () => _ = ResolveBlockAsync(ConflictChoice.Theirs), T("их", "theirs"), keywords: "конфликт блок theirs их", available: () => _conflict is not null);
        Add("conflict.both", T("Оставить обе версии блока", "Keep both versions of the block"), CommandScope.Conflict, CommandKey.Of("b", Key.B),
            () => _ = ResolveBlockAsync(ConflictChoice.Both), T("обе", "both"), keywords: "конфликт блок оба обе", available: () => _conflict is not null);
        Add("conflict.restore", T("Вернуть конфликт в файле", "Restore the conflict in the file"), CommandScope.Conflict, CommandKey.Of("r", Key.R),
            () => _ = RestoreConflictAsync(), T("вернуть", "restore"), hintOptional: true, dangerous: true, available: () => _conflict is not null,
            preview: () => _conflict is null ? null : CommandText.Format(GitService.RestoreConflictArguments(_conflict.Path)),
            keywords: "конфликт вернуть заново отменить");
        Add("conflict.back", T("Вернуться к файлам", "Back to files"), CommandScope.Conflict, CommandKey.Of("esc", Key.Esc),
            () => _files.SetFocus(), T("к файлам", "to files"), available: () => _conflict is not null);

        Add("files.stashSelected", T("Спрятать в stash только это", "Stash only this"), CommandScope.Files, CommandKey.Of("S", Key.S.WithShift),
            StashSelected, hintOptional: true, available: () => _files.SelectedRow is FileEntryRow or DirectoryRow,
            preview: () => "git stash push --include-untracked -- <путь>", keywords: "стэш спрятать файл часть выбранное отложить");
        Add("files.history", T("История файла", "File history"), CommandScope.Files, CommandKey.Of("h", Key.H),
            OpenSelectedFileHistory, T("история", "history"), hintOptional: true,
            available: () => _files.SelectedRow is FileEntryRow { Section: not FileSection.Untracked },
            keywords: "история лог коммиты файла history log blame кто менял");

        Add("files.blame", T("Blame: кто написал каждую строку", "Blame: who wrote each line"), CommandScope.Files, CommandKey.Of("B", Key.B.WithShift),
            OpenSelectedBlame, "blame", hintOptional: true,
            available: () => _files.SelectedRow is FileEntryRow { Section: not (FileSection.Untracked or FileSection.Conflicts) },
            keywords: "blame кто автор строка аннотация annotate");

        Add("diff.stageLines", T("Stage строки / выделения", "Stage line / selection"), CommandScope.Diff, CommandKey.Of("space", Key.Space),
            () => _ = ApplyLinesAsync(hunk: false), "stage", available: () => _staging is { Staged: false },
            preview: () => CommandText.Format(GitService.ApplyToIndexArguments(reverse: false)), keywords: "строка добавить");
        Add("diff.unstageLines", T("Unstage строки / выделения", "Unstage line / selection"), CommandScope.Diff, CommandKey.Of("space", Key.Space),
            () => _ = ApplyLinesAsync(hunk: false), "unstage", available: () => _staging is { Staged: true },
            preview: () => CommandText.Format(GitService.ApplyToIndexArguments(reverse: true)), keywords: "строка убрать");
        Add("diff.stageHunk", T("Stage фрагмента (@@)", "Stage hunk (@@)"), CommandScope.Diff, CommandKey.Of("a", Key.A),
            () => _ = ApplyLinesAsync(hunk: true), T("фрагмент", "hunk"), available: () => _staging is { Staged: false },
            preview: () => CommandText.Format(GitService.ApplyToIndexArguments(reverse: false)), keywords: "hunk ханк блок");
        Add("diff.unstageHunk", T("Unstage фрагмента (@@)", "Unstage hunk (@@)"), CommandScope.Diff, CommandKey.Of("a", Key.A),
            () => _ = ApplyLinesAsync(hunk: true), T("фрагмент", "hunk"), available: () => _staging is { Staged: true },
            preview: () => CommandText.Format(GitService.ApplyToIndexArguments(reverse: true)), keywords: "hunk ханк блок");
        Add("diff.range", T("Выделить несколько строк", "Select several lines"), CommandScope.Diff, CommandKey.Of("v", Key.V),
            () => _diff.ToggleRange(), T("выделить", "select"), available: () => _staging is not null, keywords: "выделение диапазон");
        Add("diff.discardLines", T("Сбросить строки в рабочей копии", "Discard lines in the working tree"), CommandScope.Diff, CommandKey.Of("d", Key.D),
            () => _ = DiscardLinesAsync(), T("сбросить", "discard"), hintOptional: true, dangerous: true,
            available: () => _staging is { Staged: false },
            preview: () => CommandText.Format(GitService.DiscardLinesArguments), keywords: "откатить удалить строку discard");
        Add("diff.toStaged", T("Перейти к staged-части файла", "Go to the staged part of the file"), CommandScope.Diff, CommandKey.Of("tab", Key.Tab),
            SwitchStagingSide, "staged", hintOptional: true, available: () => _staging is { Staged: false } t && HasOtherSide(t),
            keywords: "переключить индекс staged сторона");
        Add("diff.toWorktree", T("Перейти к не staged изменениям файла", "Go to the unstaged changes of the file"), CommandScope.Diff, CommandKey.Of("tab", Key.Tab),
            SwitchStagingSide, T("изменения", "changes"), hintOptional: true, available: () => _staging is { Staged: true } t && HasOtherSide(t),
            keywords: "переключить рабочая копия unstaged сторона");
        Add("diff.back", T("Вернуться к файлам", "Back to files"), CommandScope.Diff, CommandKey.Of("esc", Key.Esc),
            () => _files.SetFocus(), T("к файлам", "to files"), available: () => _staging is not null);

        Add("branches.checkout", T("Checkout ветки", "Checkout branch"), CommandScope.Branches, CommandKey.Of("space", Key.Space),
            () => _ = SwitchToSelectedAsync(), "checkout", preview: () => BranchPreview(b => ["switch", "--no-guess", b.Name]), keywords: "переключить чекаут ветка");
        Add("branches.new", T("Новая ветка", "New branch"), CommandScope.Branches, CommandKey.Of("n", Key.N),
            OpenNewBranchDialog, T("новая", "new"), preview: () => T("git switch --create <имя>", "git switch --create <name>"), keywords: "создать бранч");
        Add("branches.merge", T("Merge в текущую", "Merge into current"), CommandScope.Branches, CommandKey.Of("M", Key.M.WithShift),
            () => _ = MergeSelectedAsync(), "merge", preview: () => BranchPreview(b => ["merge", "--no-edit", b.Name]), keywords: "мерж слить влить");
        Add("branches.delete", T("Удалить ветку", "Delete branch"), CommandScope.Branches, CommandKey.Of("D", Key.D.WithShift),
            () => _ = DeleteSelectedBranchAsync(), T("удалить", "delete"), dangerous: true,
            preview: () => BranchPreview(b => GitService.DeleteBranchArguments(b, force: false)), keywords: "удалить бранч");

        Add("branches.compare", T("Сравнить с текущей веткой", "Compare with the current branch"), CommandScope.Branches,
            CommandKey.Char('='), CompareSelectedBranch, T("сравнить", "compare"), hintOptional: true,
            available: () => _branches.Selected is { IsCurrent: false },
            keywords: "сравнить разница diff ветки что изменилось compare pr");
        Add("branches.rename", T("Переименовать ветку", "Rename branch"), CommandScope.Branches, CommandKey.Of("R", Key.R.WithShift),
            RenameSelectedBranch, T("переименовать", "rename"), hintOptional: true,
            preview: () => BranchPreview(b => GitService.RenameBranchArguments(b, T("<имя>", "<name>"))), keywords: "переименовать имя ветки rename move");
        Add("branches.deleteMerged", T("Удалить ветки, влитые через rebase / squash", "Delete branches merged by rebase / squash"), CommandScope.Branches,
            CommandKey.Of("X", Key.X.WithShift), DeleteMergedCopies, T("убрать влитые", "clean merged"), hintOptional: true,
            available: () => _branches.Items.Any(b => !b.IsCurrent && _mergedCopies.ContainsKey(b.Name)),
            keywords: "удалить влитые ветки очистить rebase squash merged cleanup prune");
        Add("branches.cherryPick", T("Cherry-pick вершины ветки", "Cherry-pick branch tip"), CommandScope.Branches, CommandKey.Of("C", Key.C.WithShift),
            () => _ = CherryPickBranchTipAsync(), "cherry-pick", hintOptional: true, available: () => _repoOperation == RepoOperation.None,
            preview: () => BranchPreview(b => ["cherry-pick", b.Name]), keywords: "черри пик скопировать коммит перенести");

        Add("commits.amend", T("Amend: добавить staged в последний коммит", "Amend: add staged to the last commit"), CommandScope.Commits, CommandKey.Of("a", Key.A),
            () => _ = AmendAsync(), "amend", dangerous: true, preview: () => CommandText.Format(GitService.AmendArguments),
            keywords: "дополнить исправить последний коммит аменд");
        Add("commits.revert", T("Revert коммита", "Revert commit"), CommandScope.Commits, CommandKey.Of("v", Key.V),
            () => _ = RevertSelectedAsync(), "revert", preview: () => CommitPreview(GitService.RevertArguments),
            keywords: "откатить отменить коммит ревёрт");
        Add("commits.resetSoft", T("Reset --soft к коммиту", "Reset --soft to commit"), CommandScope.Commits, CommandKey.Of("S", Key.S.WithShift),
            () => _ = ResetSelectedAsync(ResetMode.Soft), "soft", hintOptional: true, dangerous: true,
            preview: () => CommitPreview(c => GitService.ResetArguments(c, ResetMode.Soft)), keywords: "сброс reset мягкий вернуться");
        Add("commits.resetMixed", T("Reset --mixed к коммиту", "Reset --mixed to commit"), CommandScope.Commits, CommandKey.Of("m", Key.M),
            () => _ = ResetSelectedAsync(ResetMode.Mixed), "mixed", hintOptional: true, dangerous: true,
            preview: () => CommitPreview(c => GitService.ResetArguments(c, ResetMode.Mixed)), keywords: "сброс reset вернуться");
        Add("commits.resetHard", T("Reset --hard к коммиту", "Reset --hard to commit"), CommandScope.Commits, CommandKey.Of("H", Key.H.WithShift),
            () => _ = ResetSelectedAsync(ResetMode.Hard), "hard", hintOptional: true, dangerous: true,
            preview: () => CommitPreview(c => GitService.ResetArguments(c, ResetMode.Hard)), keywords: "сброс reset жёсткий удалить вернуться");
        Add("commits.cherryPick", T("Cherry-pick по хешу или имени", "Cherry-pick by hash or name"), CommandScope.Commits, CommandKey.Of("C", Key.C.WithShift),
            OpenCherryPickDialog, "cherry-pick", hintOptional: true, preview: () => T("git cherry-pick <хеш>", "git cherry-pick <hash>"),
            available: () => _repoOperation == RepoOperation.None, keywords: "черри пик скопировать коммит перенести");
        Add("commits.fixup", T("Встроить staged-изменения в этот коммит (fixup)", "Put staged changes into this commit (fixup)"), CommandScope.Commits,
            CommandKey.Of("F", Key.F.WithShift), () => _ = FixupSelectedAsync(), "fixup", hintOptional: true, dangerous: true,
            available: () => _repoOperation == RepoOperation.None,
            preview: () => _commits.Selected is { } c ? CommandText.Format(GitService.FixupCommitArguments(c)) + " && git rebase -i --autosquash …" : null,
            keywords: "fixup исправить старый коммит встроить autosquash дополнить поправить прошлый");
        Add("commits.compare", T("Изменения с этого коммита до HEAD", "Changes from this commit to HEAD"), CommandScope.Commits,
            CommandKey.Char('='), CompareSelectedCommit, T("сравнить", "compare"), hintOptional: true,
            available: () => _commits.Selected is { } c && _commits.Items.Count > 0 && _commits.Items[0].Hash != c.Hash,
            keywords: "сравнить разница diff коммиты что изменилось с тех пор compare");
        Add("commits.rebase", T("Interactive rebase от коммита", "Interactive rebase from commit"), CommandScope.Commits, CommandKey.Of("i", Key.I),
            () => _ = StartInteractiveRebaseAsync(), "rebase -i", dangerous: true, available: () => _repoOperation == RepoOperation.None,
            preview: () => CommitPreview(GitService.InteractiveRebaseArguments),
            keywords: "ребейз интерактивный squash сквош fixup объединить склеить reword переименовать drop удалить коммит порядок переставить edit");

        Add("stash.pop", T("Применить и удалить stash", "Apply and drop stash"), CommandScope.Stash, CommandKey.Of("space", Key.Space),
            () => _ = StashActionAsync(pop: true), "pop", preview: () => StashPreview("pop"), keywords: "стэш достать pop");
        Add("stash.apply", T("Применить stash", "Apply stash"), CommandScope.Stash, CommandKey.Of("a", Key.A),
            () => _ = StashActionAsync(pop: false), "apply", preview: () => StashPreview("apply"), keywords: "стэш apply");
        Add("stash.drop", T("Удалить stash", "Drop stash"), CommandScope.Stash, CommandKey.Of("d", Key.D),
            () => _ = DropStashAsync(), T("удалить", "delete"), dangerous: true, preview: () => StashPreview("drop"), keywords: "стэш drop");

        Add("operation.abort", T("Прервать merge / cherry-pick / rebase", "Abort merge / cherry-pick / rebase"), CommandScope.Global, CommandKey.Of("A", Key.A.WithShift),
            () => _ = AbortOperationAsync(), T("прервать", "abort"), dangerous: true,
            available: () => _repoOperation != RepoOperation.None,
            preview: () => _repoOperation == RepoOperation.None ? null : CommandText.Format(GitService.AbortArguments(_repoOperation)),
            keywords: "abort отменить merge конфликт");
        Add("operation.skip", T("Пропустить текущий коммит rebase / cherry-pick / revert", "Skip the current commit of rebase / cherry-pick / revert"),
            CommandScope.Global, CommandKey.Of("K", Key.K.WithShift), () => _ = SkipOperationCommitAsync(), T("пропустить", "skip"), hintOptional: true,
            dangerous: true, available: () => _repoOperation is RepoOperation.Rebase or RepoOperation.CherryPick or RepoOperation.Revert,
            preview: () => _repoOperation is RepoOperation.Rebase or RepoOperation.CherryPick or RepoOperation.Revert
                ? CommandText.Format(GitService.SkipArguments(_repoOperation)) : null,
            keywords: "skip пропустить коммит пустой rebase cherry-pick");
        Add("operation.continue", T("Продолжить merge / cherry-pick / rebase", "Continue merge / cherry-pick / rebase"), CommandScope.Global, CommandKey.Of("C", Key.C.WithShift),
            () => _ = ContinueOperationAsync(), T("продолжить", "continue"),
            available: () => _repoOperation != RepoOperation.None,
            preview: () => _repoOperation == RepoOperation.None ? null : CommandText.Format(GitService.ContinueArguments(_repoOperation)),
            keywords: "continue дальше продолжить rebase конфликт решён");
        Add("commit", T("Коммит", "Commit"), CommandScope.Global, CommandKey.Of("c", Key.C),
            OpenCommitDialog, T("коммит", "commit"), preview: () => "git commit --file=- --cleanup=strip");
        Add("repo.createGitIgnore", T("Создать .gitignore (Visual Studio)", "Create .gitignore (Visual Studio)"), CommandScope.Global, CommandKey.Of("I", Key.I.WithShift),
            () => _ = CreateGitIgnoreAsync(),
            ".gitignore", hintOptional: true, preview: () => $"GET {GitIgnoreSource.Url}", available: () => !RepoScaffold.GitIgnoreExists(_git.RepoRoot),
            keywords: "гитигнор игнор visual studio шаблон создать файл ignore", unavailableNote: T("уже есть", "already exists"));
        Add("repo.createReadme", T("Создать README.md", "Create README.md"), CommandScope.Global, CommandKey.Of("R", Key.R.WithShift),
            () => CreateScaffoldFile(RepoScaffold.CreateReadme, RepoScaffold.ReadmeFileName, T("Добавить README", "Add README")),
            "README", hintOptional: true, available: () => !RepoScaffold.ReadmeExists(_git.RepoRoot),
            keywords: "ридми readme описание создать файл", unavailableNote: T("уже есть", "already exists"));
        Add("sync.pull", "Pull", CommandScope.Global, CommandKey.Of("p", Key.P),
            () => _ = PullAsync(), "pull", hintOptional: true, preview: () => "git pull --no-edit", keywords: "пул получить скачать");
        Add("sync.push", "Push / push -u", CommandScope.Global, CommandKey.Of("P", Key.P.WithShift),
            () => _ = PushAsync(), "push", hintOptional: true, preview: () => "git push", keywords: "пуш отправить опубликовать upstream");
        Add("stash.save", T("Спрятать изменения в stash", "Stash changes"), CommandScope.Global, CommandKey.Of("s", Key.S),
            OpenStashDialog, "stash", hintOptional: true, preview: () => "git stash push --include-untracked",
            keywords: "стэш спрятать отложить сохранить");
        Add("sync.fetch", "Fetch", CommandScope.Global, CommandKey.Of("f", Key.F),
            () => _ = FetchAsync(), preview: () => "git fetch --all --prune", keywords: "фетч обновить сервер");

        Add("nav.next", T("Следующая панель", "Next panel"), CommandScope.Global, CommandKey.Of("tab", Key.Tab),
            () => FocusPanel(+1), T("панель", "panel"), hintOptional: true, hidden: true);
        Add("nav.previous", T("Предыдущая панель", "Previous panel"), CommandScope.Global, CommandKey.Of("shift+tab", Key.Tab.WithShift),
            () => FocusPanel(-1), hidden: true);
        Add("nav.files", T("Панель файлов", "Files panel"), CommandScope.Global, CommandKey.Of("1", Key.D1), () => _files.SetFocus(), hidden: true);
        Add("nav.branches", T("Панель веток", "Branches panel"), CommandScope.Global, CommandKey.Of("2", Key.D2), () => _branches.SetFocus(), hidden: true);
        Add("nav.commits", T("Панель коммитов", "Commits panel"), CommandScope.Global, CommandKey.Of("3", Key.D3), () => _commits.SetFocus(), hidden: true);
        Add("nav.stash", T("Панель stash", "Stash panel"), CommandScope.Global, CommandKey.Of("4", Key.D4), () => _stash.SetFocus(), hidden: true,
            available: () => _stash.Visible);
        Add("view.diffDown", T("Листать diff вниз", "Scroll diff down"), CommandScope.Global, CommandKey.Of("ctrl+d", Key.D.WithCtrl),
            () => ScrollRight(+1), hidden: true);
        Add("view.diffUp", T("Листать diff вверх", "Scroll diff up"), CommandScope.Global, CommandKey.Of("ctrl+u", Key.U.WithCtrl),
            () => ScrollRight(-1), hidden: true);
        Add("history.undo", T("Отменить последнее действие", "Undo last action"), CommandScope.Global, CommandKey.Of("z", Key.Z),
            () => _ = UndoAsync(), dangerous: true, keywords: "undo отмена откат reflog вернуть");
        Add("view.refresh", T("Обновить", "Refresh"), CommandScope.Global, CommandKey.Of("r", Key.R), () => _ = RefreshAsync(), keywords: "обновить");
        Add("github.open", T("GitHub: pull requests", "GitHub: pull requests"), CommandScope.Global, CommandKey.Of("g", Key.G),
            () => OpenGitHub(GitHubTab.Repository), "GitHub", hintOptional: true,
            keywords: "гитхаб github pr пулл реквест pull request ревью review merge дашборд");
        Add("github.createPr", T("Создать PR из текущей ветки", "Create a PR from the current branch"), CommandScope.Global,
            CommandKey.Of("N", Key.N.WithShift), () => _ = CreatePullRequestAsync(), preview: () => T("gh pr create --head <ветка> --base <main>", "gh pr create --head <branch> --base <main>"),
            available: () => _repoStatus is { IsDetached: false, Upstream: not null },
            keywords: "гитхаб github pr пулл реквест pull request создать новый открыть");
        Add("github.actions", T("GitHub Actions: запуски, jobs, лог, перезапуск", "GitHub Actions: runs, jobs, log, rerun"), CommandScope.Global,
            CommandKey.Of("ctrl+o", Key.O.WithCtrl), OpenActions,
            available: () => _gitHubRepo is not null && _auth.IsAuthorized,
            keywords: "ci actions сборка тесты проверки лог github workflow");
        Add("view.wrap", T("Перенос длинных строк в diff", "Wrap long lines in the diff"), CommandScope.Global, CommandKey.Of("w", Key.W),
            ToggleWrap, keywords: "перенос строки длинные wrap не влезает ширина");
        Add("view.codeDetails", T("Подробнее: какие классы и методы изменились", "Details: which classes and methods changed"), CommandScope.Global,
            CommandKey.Of("ctrl+e", Key.E.WithCtrl), ToggleCodeDetails, T("подробнее", "details"), hintOptional: true,
            keywords: "подробнее методы классы что изменилось код roslyn сводка details");
        Add("dotnet.check", T("Проверка: dotnet build и test", "Check: dotnet build and test"), CommandScope.Global,
            CommandKey.Of("F5", Key.F5), OpenCheck, T("проверка", "check"), hintOptional: true,
            available: () => _dotnetTarget is not null, unavailableNote: T("нет .sln / .csproj", "no .sln / .csproj"),
            preview: () => _dotnetTarget is { } target ? $"dotnet build {target} && dotnet test {target} --no-build" : null,
            keywords: "сборка тесты build test проверка dotnet перед push собрать");
        Add("view.searchHistory", T("Поиск по истории", "Search history"), CommandScope.Global, CommandKey.Of("ctrl+f", Key.F.WithCtrl),
            OpenHistorySearch, T("поиск", "search"), hintOptional: true,
            keywords: "поиск найти коммит сообщение автор код строка grep pickaxe search find");
        Add("view.worktrees", T("Worktrees: рабочие папки для других веток", "Worktrees: working folders for other branches"), CommandScope.Global,
            CommandKey.Of("W", Key.W.WithShift), OpenWorktrees, keywords: "worktree рабочая папка вторая копия параллельно ветка");
        Add("view.tags", T("Теги", "Tags"), CommandScope.Global, CommandKey.Char('#'), OpenTags, keywords: "тег теги релиз версия tag tags release");
        Add("view.fileHistory", T("История любого файла…", "History of any file…"), CommandScope.Global, CommandKey.Of("F", Key.F.WithShift),
            PickFileHistory, keywords: "история файла лог найти файл history log file");
        Add("view.graph", T("Граф всех веток", "Graph of all branches"), CommandScope.Global, CommandKey.Of("G", Key.G.WithShift), OpenGraph, T("граф", "graph"),
            hintOptional: true, available: () => _commits.Items.Count > 0, keywords: "граф ветки история дерево git graph");
        Add("view.commandLog", "Command Log", CommandScope.Global, CommandKey.Char('~'), ToggleCommandLog, keywords: "журнал лог история команд");

        Add("app.palette", T("Палитра команд", "Command palette"), CommandScope.Global,
            CommandKey.Any("ctrl+p / :", CommandKey.Of("ctrl+p", Key.P.WithCtrl), CommandKey.Char(':')), OpenPalette, hidden: true);
        Add("app.theme", T("Сменить тему (dark / light / свои)", "Switch theme (dark / light / custom)"), CommandScope.Global, CommandKey.Of("T", Key.T.WithShift),
            SwitchTheme, keywords: "тема цвета светлая тёмная dark light оформление");
        Add("app.language", T("Сменить язык (ru / en)", "Switch language (ru / en)"), CommandScope.Global,
            CommandKey.Of("L", Key.L.WithShift), SwitchLanguage, keywords: "язык language english русский английский локализация");
        Add("app.settings", T("Настройки", "Settings"), CommandScope.Global, CommandKey.Char(','), OpenSettings, keywords: "настройки маскот параметры");
        Add("app.login", T("Войти в GitHub", "Sign in to GitHub"), CommandScope.Global, CommandKey.Of("ctrl+g", Key.G.WithCtrl),
            Login, T("войти", "sign in"), hintOptional: true, available: () => !_auth.IsAuthorized);
        Add("app.help", T("Справка (также F1)", "Help (also F1)"), CommandScope.Global,
            CommandKey.Any("?", CommandKey.Char('?'), CommandKey.Of("F1", Key.F1)), OpenHelp, T("справка", "help"));
        Add("app.back", T("Назад к выбору репо", "Back to repo selection"), CommandScope.Global, CommandKey.Of("esc", Key.Esc), RequestStop, T("назад", "back"));
        Add("app.quit", T("Выход", "Quit"), CommandScope.Global, CommandKey.Of("q", Key.Q), () =>
        {
            Result = MainResult.Quit;
            RequestStop();
        }, T("выход", "quit"));

        ReportKeyProblems(keyOverrides, overridden);
    }

    /// <summary>Находит в разделе keys неизвестные команды и клавиши, занятые другой командой того же экрана.</summary>
    private void ReportKeyProblems(IReadOnlyDictionary<string, string> keyOverrides, HashSet<string> overridden)
    {
        foreach (var id in keyOverrides.Keys.Where(id => _commands.All.All(c => c.Id != id)))
            _configProblems.Add(T($"команды {id} нет, клавишу назначить не на что", $"there is no command {id}, nothing to bind the key to"));

        foreach (var group in _commands.All.Where(c => overridden.Contains(c.Id)).GroupBy(c => c.Scope))
        {
            foreach (var command in group)
            {
                var other = _commands.All.FirstOrDefault(c => c.Id != command.Id && c.Scope == command.Scope && c.Key.Label == command.Key.Label);
                if (other is not null)
                    _configProblems.Add(T($"клавиша {command.Key.Label} назначена и на {command.Id}, и на {other.Id}", $"key {command.Key.Label} is bound to both {command.Id} and {other.Id}"));
            }
        }
    }

    /// <summary>Включает следующую тему по кругу, запоминает её в конфиге и перезапускает окно: цвета задаются при создании виджетов.</summary>
    private void SwitchTheme()
    {
        if (_busy)
            return;

        var config = _config.Load();
        var next = ThemeCatalog.Next(config);
        _config.Save(config with { Theme = next });
        ReloadNotice = T($"Тема: {next}", $"Theme: {next}");
        Result = MainResult.Reload;
        RequestStop();
    }

    /// <summary>Репозиторий для <see cref="MainResult.OpenRepo"/>.</summary>
    public string? OpenRepoPath { get; private set; }

    /// <summary>Сообщение для следующего окна после <see cref="MainResult.Reload"/>.</summary>
    public string? ReloadNotice { get; private set; }

    /// <summary>Переключает язык ru ↔ en, запоминает в конфиге и перезапускает окно: тексты задаются при создании виджетов.</summary>
    private void SwitchLanguage()
    {
        if (_busy)
            return;

        var next = Loc.Current == Language.Ru ? Language.En : Language.Ru;
        _config.Save(_config.Load() with { Language = Loc.Code(next) });
        Loc.Apply(next);
        ReloadNotice = T("Язык: русский", "Language: English");
        Result = MainResult.Reload;
        RequestStop();
    }

    /// <summary>
    /// Отменяет последнее действие по reflog: коммит, merge, pull, checkout. Переписывание истории — через подтверждение;
    /// если коммит уже отправлен, предупреждает, что понадобится force push.
    /// </summary>
    private async Task UndoAsync()
    {
        if (_busy)
            return;

        var last = await _git.GetLastReflogEntryAsync();
        var (plan, reason) = UndoPlanner.Plan(last);
        if (plan is null)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {reason}", Theme.Error));
            return;
        }

        var pushed = plan.RewritesHistory && await _git.IsHeadPushedAsync();
        App?.Invoke(() =>
        {
            if (plan.RewritesHistory)
            {
                var warning = pushed
                    ? T($"{Theme.SymbolWarning} Это уже отправлено в {_repoStatus?.Upstream}: после отмены история разойдётся, и для push понадобится --force-with-lease.", $"{Theme.SymbolWarning} This is already pushed to {_repoStatus?.Upstream}: after undo the history will diverge and push will need --force-with-lease.")
                    : T("Ветка вернётся к предыдущему состоянию из reflog. Повторить действие можно заново, а найти прежний коммит — в git reflog.", "The branch returns to its previous state from reflog. You can redo the action, and find the old commit in git reflog.");
                if (!Confirm(plan.Question, plan.Args, warning, T("Отменить", "Undo")))
                    return;
            }

            _ = RunAsync(ct => _git.UndoAsync(plan, ct), plan.Done);
        });
    }

    /// <summary>Прячет все изменения, включая новые файлы, в stash с необязательным сообщением.</summary>
    private void OpenStashDialog()
    {
        if (_busy)
            return;

        if (_files.Entries.Count == 0)
        {
            ShowStatus(T($"{Theme.SymbolError} Нечего прятать в stash: изменений нет", $"{Theme.SymbolError} Nothing to stash: no changes"), Theme.Error);
            return;
        }

        var files = _files.Entries.Select(e => e.File.Path).Distinct().Count();
        using var dialog = new InputDialog("Stash", T($"Спрятать {FilesWord(files)} (с новыми). Сообщение — по желанию", $"Stash {FilesWord(files)} (including new ones). Message is optional"),
            T("спрятать", "stash"));
        App!.Run(dialog);
        if (dialog.Result is not { } message)
            return;

        _ = RunAsync(ct => _git.StashAsync(message, ct), T($"Изменения спрятаны в stash: {FilesWord(files)}", $"Changes stashed: {FilesWord(files)}"));
    }

    private Task StashActionAsync(bool pop)
    {
        if (_stash.Selected is not { } stash)
            return Task.CompletedTask;

        return pop
            ? RunAsync(ct => _git.StashPopAsync(stash, ct), T($"Stash применён и удалён: {stash.Message}", $"Stash applied and dropped: {stash.Message}"))
            : RunAsync(ct => _git.StashApplyAsync(stash, ct), T($"Stash применён и оставлен в списке: {stash.Message}", $"Stash applied and kept in the list: {stash.Message}"));
    }

    private Task DropStashAsync()
    {
        if (_busy || _stash.Selected is not { } stash)
            return Task.CompletedTask;

        if (!Confirm(T($"Удалить stash «{stash.Message}»?", $"Drop stash '{stash.Message}'?"), GitService.StashDropArguments(stash),
                T("Спрятанные изменения пропадут. Это нельзя отменить.", "The stashed changes will be lost. This can't be undone."), T("Удалить", "Drop")))
            return Task.CompletedTask;

        return RunAsync(ct => _git.StashDropAsync(stash, ct), T($"Stash удалён: {stash.Message}", $"Stash dropped: {stash.Message}"));
    }

    private string? StashPreview(string action) =>
        _stash.Selected is { } stash ? CommandText.Format(["stash", action, stash.Ref]) : null;

    /// <summary>Показывает панель stash, только если в нём есть записи; высота — по числу записей, не больше 5.</summary>
    private void SetStashes(IReadOnlyList<StashEntry> stashes)
    {
        _stash.SetItems(stashes);
        var visible = stashes.Count > 0;
        if (!visible && _stash.HasFocus)
            _commits.SetFocus();

        var height = visible ? Math.Min(stashes.Count, 5) + 2 : 0;
        _stash.Visible = visible;
        _stashTitle.Visible = visible;
        _stash.Height = height;
        _commits.Height = Dim.Fill(2 + height);
    }

    private bool IsConflictSelected() =>
        _files.SelectedRow is FileEntryRow { Section: FileSection.Conflicts }
            or DirectoryRow { Section: FileSection.Conflicts };

    private IReadOnlyList<FileChange> SelectedConflictFiles() => _files.SelectedRow switch
    {
        FileEntryRow { Section: FileSection.Conflicts } f => [f.File],
        DirectoryRow { Section: FileSection.Conflicts } d => d.Files.Select(e => e.File).ToList(),
        _ => [],
    };

    private string? ConflictPreview(string side)
    {
        var files = SelectedConflictFiles();
        return files.Count == 0 ? null : CommandText.Format(["checkout", side, "--", .. files.Select(f => f.Path)]) + " && git add …";
    }

    /// <summary>
    /// Решает конфликт выбранного файла или папки целиком в пользу нашей или их версии.
    /// При rebase «наша» — ветка, на которую переносятся коммиты, «их» — переносимые коммиты.
    /// </summary>
    private Task ResolveConflictAsync(bool ours)
    {
        var files = SelectedConflictFiles();
        if (files.Count == 0)
            return Task.CompletedTask;

        var name = files.Count == 1 ? files[0].Path : FilesWord(files.Count);
        var side = ours ? T("наша версия", "our version") : T("их версия", "their version");
        return RunAsync(ct => _git.ResolveConflictAsync(files, ours, ct),
            T($"{name}: оставлена {side}. Вернуть конфликт: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<путь>")}", $"{name}: kept {side}. Restore the conflict: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<path>")}"));
    }

    /// <summary>Решает конфликт, оставляя обе версии подряд: сначала наши строки, затем их.</summary>
    private Task ResolveBothAsync()
    {
        var files = SelectedConflictFiles();
        if (files.Count == 0)
            return Task.CompletedTask;

        var name = files.Count == 1 ? files[0].Path : FilesWord(files.Count);
        return RunAsync(ct => _git.ResolveBothAsync(files, ct),
            T($"{name}: оставлены обе версии — проверь порядок строк. Вернуть конфликт: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<путь>")}", $"{name}: kept both versions, check the line order. Restore the conflict: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<path>")}"));
    }

    /// <summary>Прерывает незавершённую операцию после подтверждения: возвращает состояние до её начала.</summary>
    private Task AbortOperationAsync()
    {
        if (_busy || _repoOperation == RepoOperation.None)
            return Task.CompletedTask;

        var operation = _repoOperation;
        var name = RepoHeader.OperationName(operation);
        if (!Confirm(T($"Прервать {name}?", $"Abort {name}?"), GitService.AbortArguments(operation),
                T($"Ветка вернётся к состоянию до {name}. Всё, что уже сделано при разрешении конфликтов, пропадёт.", $"The branch returns to its state before {name}. Everything done while resolving conflicts will be lost."),
                T("Прервать", "Abort")))
            return Task.CompletedTask;

        return RunAsync(ct => _git.AbortAsync(operation, ct), T($"{char.ToUpper(name[0])}{name[1..]} прерван, всё как до него", $"{char.ToUpper(name[0])}{name[1..]} aborted, everything is as before"));
    }

    /// <summary>Продолжает операцию после решения конфликтов или правки коммита (edit при rebase).</summary>
    private Task ContinueOperationAsync()
    {
        if (_busy || _repoOperation == RepoOperation.None)
            return Task.CompletedTask;

        var operation = _repoOperation;
        var name = RepoHeader.OperationName(operation);
        return RunAsync(ct => _git.ContinueAsync(operation, ct), async _ =>
            await _git.GetOperationAsync() == operation
                ? await StoppedMessageAsync()
                : T($"{char.ToUpper(name[0])}{name[1..]} завершён", $"{char.ToUpper(name[0])}{name[1..]} finished"));
    }

    /// <summary>Rebase остановился на коммите с действием edit.</summary>
    private async Task<string> StoppedMessageAsync() =>
        T($"Rebase остановлен на {await _git.GetShortHeadAsync()} для правки: измени файлы, добавь в индекс, a — amend, C — продолжить", $"Rebase stopped at {await _git.GetShortHeadAsync()} for editing: change files, stage them, a amends, C continues");

    /// <summary>
    /// Interactive rebase от выбранного коммита (включительно) до HEAD: план в <see cref="RebaseDialog"/>,
    /// затем подтверждение. Диапазон с merge-коммитами не поддерживается: git выпрямил бы историю.
    /// </summary>
    private async Task StartInteractiveRebaseAsync()
    {
        if (_busy || _commits.Selected is not { } selected)
            return;

        var range = RangeFromHead(selected);
        if (range is null)
        {
            ShowStatus(T($"{Theme.SymbolError} В диапазоне есть merge-коммит: такой rebase не поддерживается. Выбери коммит выше merge", $"{Theme.SymbolError} The range has a merge commit: this rebase is not supported. Pick a commit above the merge"), Theme.Error);
            return;
        }

        IReadOnlyList<RebaseStep> steps;
        using (var dialog = new RebaseDialog(range))
        {
            App!.Run(dialog);
            if (dialog.Result is not { } result)
                return;
            steps = result;
        }

        var summary = RebaseTodo.Summary(steps, range);
        var warning = await RewriteWarningAsync(
            T("Коммиты будут пересозданы с новыми хешами. Незакоммиченные изменения спрячутся и вернутся (--autostash). ", "Commits will be recreated with new hashes. Uncommitted changes are stashed and restored (--autostash). ") +
            T("При конфликте rebase остановится: реши файлы и нажми C, или A — вернуть всё как было.", "On a conflict the rebase stops: resolve the files and press C, or A to restore everything."));
        App?.Invoke(() =>
        {
            if (!Confirm(T($"Переписать {PluralRu(range.Count, "коммит", "коммита", "коммитов")}: {summary}?", $"Rewrite {PluralEn(range.Count, "commit", "commits")}: {summary}?"),
                    GitService.InteractiveRebaseArguments(selected), warning, "Rebase"))
                return;

            _ = RunAsync(ct => _git.InteractiveRebaseAsync(steps, ct), async _ =>
                await _git.GetOperationAsync() == RepoOperation.Rebase
                    ? await StoppedMessageAsync()
                    : T($"Rebase выполнен: {summary}", $"Rebase done: {summary}"));
        });
    }

    /// <summary>
    /// Коммиты от HEAD до <paramref name="commit"/> включительно, если между ними нет merge-коммитов (цепочка по первым
    /// родителям), иначе null: rebase такой истории git бы выпрямил.
    /// </summary>
    private List<Commit>? RangeFromHead(Commit commit)
    {
        var range = _commits.Items.Take(_commits.Items.ToList().IndexOf(commit) + 1).ToList();
        var linear = range.All(c => c.Parents.Count <= 1)
            && range.Zip(range.Skip(1)).All(pair => pair.First.Parents.Count == 1 && pair.First.Parents[0] == pair.Second.Hash);
        return linear ? range : null;
    }

    /// <summary>
    /// Встраивает staged-изменения в выбранный старый коммит (commit --fixup + rebase --autosquash) после подтверждения.
    /// Для последнего коммита есть amend, для диапазона с merge — объяснение, почему нельзя.
    /// </summary>
    private async Task FixupSelectedAsync()
    {
        if (_busy || _commits.Selected is not { } commit)
            return;

        if (_repoOperation != RepoOperation.None)
        {
            ShowStatus(T($"{Theme.SymbolError} Сначала заверши {RepoHeader.OperationName(_repoOperation)}: C — продолжить, A — прервать",
                $"{Theme.SymbolError} Finish the {RepoHeader.OperationName(_repoOperation)} first: C continues, A aborts"), Theme.Error);
            return;
        }

        if (!_files.Entries.Any(r => r.Section == FileSection.Staged))
        {
            ShowStatus(T($"{Theme.SymbolError} Нечего встраивать: добавь исправления в индекс (space), затем F на коммите",
                $"{Theme.SymbolError} Nothing to put in: stage the fix (space), then F on the commit"), Theme.Error);
            return;
        }

        if (_commits.Items[0].Hash == commit.Hash)
        {
            ShowStatus(T($"{Theme.SymbolError} Это последний коммит: для него есть amend (a)", $"{Theme.SymbolError} This is the last commit: use amend (a)"), Theme.Error);
            return;
        }

        if (RangeFromHead(commit) is not { } range)
        {
            ShowStatus(T($"{Theme.SymbolError} Между коммитом и HEAD есть merge-коммит: так встроить нельзя. Выбери коммит выше merge",
                $"{Theme.SymbolError} There is a merge commit between it and HEAD: can't do it this way. Pick a commit above the merge"), Theme.Error);
            return;
        }

        var warning = await RewriteWarningAsync(T(
            $"Коммит {commit.ShortHash} и {Plural(range.Count - 1, "коммит", "коммита", "коммитов", "commit", "commits")} после него пересоздадутся с новыми хешами. " +
            "Если исправление конфликтует с более поздними коммитами, rebase остановится: реши файлы и нажми C, или A — вернуть всё как было.",
            $"Commit {commit.ShortHash} and {Plural(range.Count - 1, "коммит", "коммита", "коммитов", "commit", "commits")} after it get new hashes. " +
            "If the fix conflicts with later commits, the rebase stops: resolve the files and press C, or A to restore everything."));
        var command = CommandText.Format(GitService.FixupCommitArguments(commit)) + " && " + CommandText.Format(GitService.AutosquashArguments(commit));
        App?.Invoke(() =>
        {
            using var dialog = new ConfirmDialog(T($"Встроить staged-изменения в {commit.ShortHash} «{commit.Subject}»?",
                $"Put the staged changes into {commit.ShortHash} '{commit.Subject}'?"), command, warning, "Fixup");
            App!.Run(dialog);
            if (dialog.Result != true)
                return;

            _ = RunAsync(ct => _git.FixupAsync(commit, ct),
                T($"Изменения встроены в «{commit.Subject}», история пересобрана", $"Changes put into '{commit.Subject}', history rebuilt"),
                T("Встраиваю исправление…", "Putting the fix in…"));
        });
    }

    private void FocusPanel(int step)
    {
        View[] panels = _stash.Visible ? [_files, _branches, _commits, _stash] : [_files, _branches, _commits];
        var current = Math.Max(0, Array.FindIndex(panels, p => p.HasFocus));
        panels[(current + step + panels.Length) % panels.Length].SetFocus();
    }

    /// <summary>История файла под курсором (у переименованного — по новому имени, прежние прослеживаются).</summary>
    private void OpenSelectedFileHistory()
    {
        if (_busy || _files.SelectedRow is not FileEntryRow { Section: not FileSection.Untracked } row)
            return;

        OpenFileHistory(row.File.Path);
    }

    /// <summary>
    /// Выбор любого файла репозитория (в том числе без изменений) и его история. Оба окна открываются прямо
    /// из обработчика клавиши: внутри App.Invoke окно истории не получило бы свои загрузки.
    /// </summary>
    private void PickFileHistory()
    {
        if (_busy)
            return;

        using var picker = new FilePickerDialog(T("История файла", "File history"), ct => _git.GetTrackedFilesAsync(ct));
        App!.Run(picker);
        if (picker.Result is { } path)
            OpenFileHistory(path);
    }

    /// <summary>Blame файла под курсором в рабочей копии: незакоммиченные строки тоже видны.</summary>
    private void OpenSelectedBlame()
    {
        if (_busy || _files.SelectedRow is not FileEntryRow row)
            return;

        using var window = new BlameWindow(_git, row.File.Path);
        App!.Run(window);
    }

    private void OpenFileHistory(string path)
    {
        using var window = new FileHistoryWindow(_git, path);
        App!.Run(window);
    }

    /// <summary>Текущая ветка против выбранной: по умолчанию — что принесёт выбранная (от общего предка, как в PR).</summary>
    private void CompareSelectedBranch()
    {
        if (_busy || _branches.Selected is not { IsCurrent: false } branch)
            return;

        using var window = new CompareWindow(_git, _repoStatus?.Branch ?? "HEAD", branch.Name);
        App!.Run(window);
    }

    /// <summary>Выбранный коммит против HEAD: всё, что изменилось после него.</summary>
    private void CompareSelectedCommit()
    {
        if (_busy || _commits.Selected is not { } commit)
            return;

        using var window = new CompareWindow(_git, commit.Hash, "HEAD");
        App!.Run(window);
    }

    private void OpenHistorySearch()
    {
        if (_busy)
            return;

        using var window = new HistorySearchWindow(_git);
        App!.Run(window);
    }

    /// <summary>Сборка и тесты по F5: окно с выводом по мере выполнения. Ничего не запускается само.</summary>
    private void OpenCheck()
    {
        if (_busy || _dotnetTarget is not { } target)
            return;

        using var window = new CheckWindow(_git.RepoRoot, target, _excludedTests);
        App!.Run(window);
    }

    private void ToggleWrap()
    {
        _diff.Wrap = !_diff.Wrap;
        _config.Save(_config.Load() with { WrapDiff = _diff.Wrap });
        ShowStatus(_diff.Wrap ? T("Длинные строки в diff переносятся (w — обрезать)", "Long lines in the diff wrap (w to cut)")
            : T("Длинные строки в diff обрезаются (w — переносить)", "Long lines in the diff are cut (w to wrap)"), Theme.Muted, autoHide: true);
    }

    private void ToggleCodeDetails()
    {
        _codeDetails = !_codeDetails;
        ShowStatus(_codeDetails
            ? T($"{Theme.SymbolSuccess} Подробно: над diff — изменённые классы и методы (ctrl+e — выключить)", $"{Theme.SymbolSuccess} Details: changed classes and methods above the diff (ctrl+e turns off)")
            : T("Подробности о коде выключены", "Code details off"), Theme.Muted, autoHide: true);
        _diffKey = null;
        _ = LoadDiffAsync();
    }

    /// <summary>
    /// Ищет ветки, влитые в основную копиями (rebase / squash), — в фоне после обновления экрана. Основная — origin/main
    /// (влитое на GitHub видно сразу после fetch), иначе локальная; проверки кэшируются по вершинам веток.
    /// </summary>
    private async Task UpdateMergedCopiesAsync()
    {
        if (_mergedLoading)
            return;

        _mergedLoading = true;
        try
        {
            var tips = await _git.GetBranchTipsAsync();
            var main = await _git.GetRemoteDefaultBranchAsync() ?? (tips.ContainsKey("main") ? "main" : tips.ContainsKey("master") ? "master" : null);
            if (main is null)
                return;

            var baseRef = tips.ContainsKey($"origin/{main}") ? $"origin/{main}" : main;
            if (!tips.TryGetValue(baseRef, out var baseTip))
                return;

            var found = new Dictionary<string, MergedCopy>();
            foreach (var (name, tip) in tips.Where(t => t.Key != main && t.Key != $"origin/{main}"))
            {
                var key = $"{name}\0{tip}\0{baseTip}";
                if (!_mergedCache.TryGetValue(key, out var copy))
                {
                    copy = await _git.FindMergedCopyAsync(name, tip, baseRef);
                    _mergedCache[key] = copy;
                }

                if (copy is not null)
                    found[name] = copy;
            }

            App?.Invoke(() =>
            {
                _mergedCopies = found;
                _branches.Merged = found;
                _commits.Merged = found;
                _branches.SetNeedsDraw();
                _commits.SetNeedsDraw();
            });
        }
        catch (GitCommandException)
        {
            // Подписи «≈ влита» — подсказка: если git не ответил, их просто нет.
        }
        finally
        {
            _mergedLoading = false;
        }
    }

    /// <summary>Удаляет локальные ветки, влитые копиями (rebase / squash), кроме текущей — после подтверждения со списком.</summary>
    private void DeleteMergedCopies()
    {
        if (_busy)
            return;

        var branches = _branches.Items.Where(b => !b.IsCurrent && _mergedCopies.ContainsKey(b.Name)).ToList();
        if (branches.Count == 0)
        {
            ShowStatus(T("Веток, влитых через rebase или squash, нет", "No branches merged by rebase or squash"), Theme.Muted, autoHide: true);
            return;
        }

        var names = string.Join(", ", branches.Select(b => b.Name));
        if (!Confirm(T($"Удалить {Plural(branches.Count, "влитую ветку", "влитые ветки", "влитых веток", "merged branch", "merged branches")}?",
                    $"Delete {Plural(branches.Count, "влитую ветку", "влитые ветки", "влитых веток", "merged branch", "merged branches")}?"),
                ["branch", "-D", .. branches.Select(b => b.Name)],
                T($"{names}. Их изменения уже в основной ветке (через rebase или squash), так что ничего не теряется. На сервере ветки не удаляются.",
                    $"{names}. Their changes are already in the base branch (via rebase or squash), nothing is lost. Branches on the server are not deleted."),
                T("Удалить", "Delete")))
            return;

        _ = RunAsync(ct => _git.DeleteBranchesAsync(branches, ct),
            T($"Удалено: {names}", $"Deleted: {names}"));
    }

    /// <summary>Выбор у конца загруженного лога, а коммитов там ровно сколько грузили, — значит, есть ещё: грузим следующую порцию.</summary>
    private void LoadMoreCommitsIfAtEnd()
    {
        var count = _commits.Items.Count;
        if (count < _logLimit || _commits.SelectedIndex < count - 5)
            return;

        _logLimit += LogPage;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T($"Загружаю ещё коммиты (до {_logLimit})…", $"Loading more commits (up to {_logLimit})…")}", Theme.Muted, autoHide: true);
        _ = RefreshAsync(silent: true);
    }

    /// <summary>Окно worktrees; enter в нём — открыть другую рабочую папку в LGit.</summary>
    private void OpenWorktrees()
    {
        if (_busy)
            return;

        using var window = new WorktreesWindow(_git);
        App!.Run(window);
        if (window.OpenPath is { } path)
        {
            OpenRepoPath = path;
            Result = MainResult.OpenRepo;
            RequestStop();
        }
        else if (window.Changed)
        {
            _ = RefreshAsync();
        }
    }

    /// <summary>Окно тегов: создать на HEAD, отправить, удалить.</summary>
    private void OpenTags()
    {
        if (_busy)
            return;

        using var window = new TagsWindow(_git);
        App!.Run(window);
        if (window.Changed)
            _ = RefreshAsync();
    }

    /// <summary>Переименовывает выбранную ветку; имя проверяется так же, как у новой.</summary>
    private void RenameSelectedBranch()
    {
        if (_busy || _branches.Selected is not { } branch)
            return;

        var others = _branches.Items.Where(b => b.Name != branch.Name).Select(b => b.Name).ToList();
        using var dialog = new InputDialog(T("Переименовать ветку", "Rename branch"),
            branch.Upstream is null
                ? T($"Сейчас: {branch.Name}", $"Now: {branch.Name}")
                : T($"Сейчас: {branch.Name}. Ветка на сервере ({branch.Upstream}) не переименуется", $"Now: {branch.Name}. The server branch ({branch.Upstream}) is not renamed"),
            T("переименовать", "rename"), name => name == branch.Name ? T("Это то же имя", "Same name") : BranchName.Check(name, others), initial: branch.Name);
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return;

        _ = RunAsync(ct => _git.RenameBranchAsync(branch, name, ct), T($"Ветка {branch.Name} теперь {name}", $"Branch {branch.Name} is now {name}"));
    }

    /// <summary>Прячет в stash только файл или папку под курсором (с новыми файлами), остальные изменения остаются.</summary>
    private void StashSelected()
    {
        if (_busy)
            return;

        var (paths, name) = _files.SelectedRow switch
        {
            FileEntryRow f => ((IReadOnlyList<string>)[f.File.Path], f.File.Path),
            DirectoryRow d => (d.Files.Select(e => e.File.Path).Distinct().ToList(), $"{d.Path}/"),
            _ => ([], ""),
        };
        if (paths.Count == 0)
            return;

        using var dialog = new InputDialog("Stash", T($"Спрятать только {name}. Сообщение — по желанию", $"Stash only {name}. Message is optional"),
            T("спрятать", "stash"));
        App!.Run(dialog);
        if (dialog.Result is not { } message)
            return;

        _ = RunAsync(ct => _git.StashPathsAsync(message, paths, ct), T($"{name} спрятан в stash, остальное на месте", $"{name} stashed, the rest stays"));
    }

    /// <summary>Пропускает текущий коммит rebase / cherry-pick / revert после подтверждения: его изменения выбрасываются.</summary>
    private Task SkipOperationCommitAsync()
    {
        if (_busy || _repoOperation is not (RepoOperation.Rebase or RepoOperation.CherryPick or RepoOperation.Revert))
            return Task.CompletedTask;

        var operation = _repoOperation;
        if (!Confirm(T("Пропустить текущий коммит?", "Skip the current commit?"), GitService.SkipArguments(operation),
                T($"Изменения этого коммита не попадут в результат ({RepoHeader.OperationName(operation)} пойдёт дальше без него). Обычно так делают с коммитом, ставшим пустым.",
                    $"This commit's changes won't be applied ({RepoHeader.OperationName(operation)} goes on without it). Usually done for a commit that became empty."),
                T("Пропустить", "Skip")))
            return Task.CompletedTask;

        return RunAsync(ct => _git.SkipAsync(operation, ct), async _ =>
            await _git.GetOperationAsync() == operation
                ? T("Коммит пропущен, операция продолжается", "Commit skipped, the operation goes on")
                : T("Коммит пропущен, операция завершена", "Commit skipped, the operation is finished"));
    }

    /// <summary>Окно графа всех веток. Действия в нём выполняются сразу, после закрытия экран обновляется.</summary>
    private void OpenGraph()
    {
        if (_busy)
            return;

        using var graph = new GraphWindow(_git, _mergedCopies);
        App!.Run(graph);
        if (graph.Result)
            _ = RefreshAsync();
    }

    private void ToggleCommandLog()
    {
        SetLogVisible(!_log.Visible);
        _config.Save(_config.Load() with { ShowCommandLog = _log.Visible });
    }

    private void Login()
    {
        if (!_auth.GhInstalled)
        {
            ShowStatus(T($"{Theme.SymbolError} gh не найден. Установи: {GhInstallHint.Command()}", $"{Theme.SymbolError} gh not found. Install it: {GhInstallHint.Command()}"), Theme.Error);
            return;
        }

        Result = MainResult.Login;
        RequestStop();
    }

    private void OpenHelp()
    {
        using var help = new HelpDialog(Viewport.Height,
            _commands.All.Where(c => c.Scope is CommandScope.Diff or CommandScope.Conflict || HelpLayout.IsShown(c)).ToList());
        App!.Run(help);
    }

    /// <summary>Палитра команд: выбранная команда выполняется после закрытия окна.</summary>
    private void OpenPalette()
    {
        var commands = _commands.All.Where(c => !c.HiddenInPalette && c.IsAvailable()).ToList();
        using var palette = new CommandPalette(commands);
        App!.Run(palette);
        palette.Result?.Execute();
    }

    private string? BranchPreview(Func<Branch, string[]> args) =>
        _branches.Selected is { } branch ? CommandText.Format(args(branch)) : null;

    private string? StagePreview()
    {
        var (section, files) = _files.SelectedRow switch
        {
            FileEntryRow f => (f.Section, (IReadOnlyList<FileChange>)[f.File]),
            DirectoryRow d => (d.Section, d.Files.Select(e => e.File).ToList()),
            _ => (default(FileSection), []),
        };
        if (files.Count == 0)
            return null;

        return section == FileSection.Staged
            ? CommandText.Format(["restore", "--staged", "--", .. files.Select(f => f.Path)])
            : CommandText.Format(["add", "--", .. files.Select(f => f.Path)]);
    }

    /// <summary>Показывает или прячет Command Log; diff занимает освободившееся место.</summary>
    private void SetLogVisible(bool visible)
    {
        _log.Visible = visible;
        _logTitle.Visible = visible;
        _diff.Height = Dim.Fill(visible ? 2 + LogHeight : 2);
        _branchLog.Height = _diff.Height;
        _conflictPanel.Height = _diff.Height;
    }

    /// <summary>Окно настроек; выбор сразу применяется и сохраняется в конфиг.</summary>
    private void OpenSettings()
    {
        using var dialog = new SettingsDialog(_diff.Mascot);
        App!.Run(dialog);
        if (dialog.Result is not { } mascot)
            return;

        _diff.Mascot = mascot;
        _config.Save(_config.Load() with { Mascot = mascot });
    }

    /// <summary>Checkout выбранной ветки.</summary>
    private Task SwitchToSelectedAsync()
    {
        if (_branches.Selected is not { } branch)
            return Task.CompletedTask;

        if (branch.IsCurrent)
        {
            ShowStatus(T($"{Theme.SymbolSuccess} Ты уже на ветке {branch.Name}", $"{Theme.SymbolSuccess} You are already on {branch.Name}"), Theme.Muted, autoHide: true);
            return Task.CompletedTask;
        }

        return RunAsync(ct => _git.SwitchAsync(branch, ct), T($"Переключено на {branch.Name}", $"Switched to {branch.Name}"));
    }

    /// <summary>Создаёт ветку от текущего HEAD и переключается на неё.</summary>
    private void OpenNewBranchDialog()
    {
        if (_busy)
            return;

        var head = _repoStatus?.HeadHash is { Length: >= 7 } hash ? $" {Theme.SymbolSeparator} {hash[..7]}" : "";
        var from = (_repoStatus?.Branch ?? "HEAD") + head;
        var existing = _branches.Items.Select(b => b.Name).ToList();
        using var dialog = new InputDialog(T("Новая ветка", "New branch"), T($"От: {from}", $"From: {from}"), T("создать и переключиться", "create and switch"),
            name => BranchName.Check(name, existing));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return;

        _ = RunAsync(ct => _git.CreateBranchAsync(name, ct), T($"Создана ветка {name}, ты на ней", $"Created branch {name}, you are on it"));
    }

    /// <summary>
    /// Удаляет выбранную ветку после подтверждения. Если в ней есть невлитые коммиты, git откажет —
    /// тогда второе подтверждение предлагает удалить принудительно и говорит, сколько коммитов пропадёт.
    /// </summary>
    private async Task DeleteSelectedBranchAsync()
    {
        if (_busy || _branches.Selected is not { } branch)
            return;

        if (branch.IsCurrent)
        {
            ShowStatus(T($"{Theme.SymbolError} Нельзя удалить текущую ветку. Переключись на другую (space) и повтори", $"{Theme.SymbolError} Can't delete the current branch. Switch to another one (space) and retry"), Theme.Error);
            return;
        }

        if (!Confirm(T($"Удалить ветку {branch.Name}?", $"Delete branch {branch.Name}?"), GitService.DeleteBranchArguments(branch, force: false),
                T("Если в ветке есть коммиты, которых нет в текущей, git откажет и предложит удалить принудительно.", "If the branch has commits that are not in the current one, git will refuse and offer to force delete."),
                T("Удалить", "Delete")))
            return;

        var result = await RunAsync(ct => _git.DeleteBranchAsync(branch, force: false, ct), T($"Ветка {branch.Name} удалена", $"Branch {branch.Name} deleted"));
        if (result is null || result.Success || !result.StdErr.Contains("is not fully merged"))
            return;

        var unmerged = await _git.CountUnmergedAsync(branch);
        App?.Invoke(() =>
        {
            if (Confirm(T($"Ветка {branch.Name} не влита. Удалить всё равно?", $"Branch {branch.Name} is not merged. Delete anyway?"), GitService.DeleteBranchArguments(branch, force: true),
                    T($"Коммитов, которых нет в текущей ветке: {unmerged}. Они пропадут, вернуть их можно только через git reflog.", $"Commits not in the current branch: {unmerged}. They will be lost, only git reflog can bring them back."),
                    T("Удалить всё равно", "Delete anyway")))
                _ = RunAsync(ct => _git.DeleteBranchAsync(branch, force: true, ct), T($"Ветка {branch.Name} удалена", $"Branch {branch.Name} deleted"));
        });
    }

    /// <summary>Вливает выбранную ветку в текущую.</summary>
    private Task MergeSelectedAsync()
    {
        if (_branches.Selected is not { } branch)
            return Task.CompletedTask;

        if (branch.IsCurrent)
        {
            ShowStatus(T($"{Theme.SymbolError} Это текущая ветка. Выбери ветку, которую нужно влить в {branch.Name}", $"{Theme.SymbolError} This is the current branch. Pick a branch to merge into {branch.Name}"), Theme.Error);
            return Task.CompletedTask;
        }

        var target = _repoStatus?.Branch ?? "HEAD";
        return RunAsync(ct => _git.MergeAsync(branch, ct), result => Task.FromResult(
            result.StdOut.Contains("Already up to date") ? T($"{target} уже содержит все коммиты {branch.Name}", $"{target} already has all commits of {branch.Name}")
            : result.StdOut.Contains("Fast-forward") ? T($"{branch.Name} влита в {target} (fast-forward)", $"{branch.Name} merged into {target} (fast-forward)")
            : T($"{branch.Name} влита в {target}, создан merge-коммит", $"{branch.Name} merged into {target}, merge commit created")));
    }

    /// <summary>
    /// Создаёт .gitignore по актуальному шаблону Visual Studio с GitHub; без сети — по встроенному.
    /// Файл создаётся в UI-потоке, поэтому проверка «уже есть» повторяется после загрузки.
    /// </summary>
    private async Task CreateGitIgnoreAsync()
    {
        if (_busy)
            return;

        _busy = true;
        ShowStatus(T("Загружаю актуальный шаблон .gitignore с GitHub…", "Downloading the latest .gitignore template from GitHub…"), Theme.Info);
        GitIgnoreResult template;
        try
        {
            using var http = new HttpClient();
            template = await GitIgnoreSource.LoadAsync(http);
        }
        finally
        {
            _busy = false;
        }

        App?.Invoke(() =>
        {
            if (template.Origin == GitIgnoreOrigin.GitHub)
            {
                CreateScaffoldFile(root => RepoScaffold.CreateGitIgnore(root, template.Content), GitIgnoreTemplate.FileName,
                    T("Добавить .gitignore", "Add .gitignore"), T("по актуальному шаблону github/gitignore", "from the latest github/gitignore template"));
                return;
            }

            CreateScaffoldFile(root => RepoScaffold.CreateGitIgnore(root, template.Content), GitIgnoreTemplate.FileName,
                T("Добавить .gitignore", "Add .gitignore"), T($"{Theme.SymbolWarning} GitHub недоступен, взят встроенный шаблон", $"{Theme.SymbolWarning} GitHub unavailable, using the built-in template"), warning: true);
        });
    }

    /// <summary>Создаёт файл из шаблона в корне репозитория и подсказывает, как его закоммитить.</summary>
    private void CreateScaffoldFile(Func<string, bool> create, string fileName, string templateName, string? note = null,
        bool warning = false)
    {
        try
        {
            if (!create(_git.RepoRoot))
            {
                ShowStatus(T($"{Theme.SymbolError} {fileName} уже есть в репозитории", $"{Theme.SymbolError} {fileName} already exists in the repository"), Theme.Error);
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowStatus(T($"{Theme.SymbolError} Не удалось создать {fileName}: {ex.Message}", $"{Theme.SymbolError} Couldn't create {fileName}: {ex.Message}"), Theme.Error);
            return;
        }

        var how = note is null ? "" : $" ({note})";
        ShowStatus(T($"{(warning ? Theme.SymbolWarning : Theme.SymbolSuccess)} {fileName} создан{how}: space — в индекс, c — коммит (ctrl+t → «{templateName}»)", $"{(warning ? Theme.SymbolWarning : Theme.SymbolSuccess)} {fileName} created{how}: space stages, c commits (ctrl+t → '{templateName}')"),
            warning ? Theme.Warning : Theme.Success, autoHide: true);
        _ = RefreshAsync();
    }

    private string? CommitPreview(Func<Commit, string[]> args) =>
        _commits.Selected is { } commit ? CommandText.Format(args(commit)) : null;

    /// <summary>Предупреждение для действий, переписывающих историю: если коммиты уже отправлены, понадобится force push.</summary>
    private async Task<string> RewriteWarningAsync(string normal)
    {
        var pushed = await _git.IsHeadPushedAsync();
        return pushed
            ? T($"{Theme.SymbolWarning} Коммиты уже отправлены в {_repoStatus?.Upstream}: после этого история разойдётся, и для push понадобится --force-with-lease.", $"{Theme.SymbolWarning} The commits are already pushed to {_repoStatus?.Upstream}: after this the history will diverge and push will need --force-with-lease.")
            : normal;
    }

    /// <summary>Добавляет staged-изменения в последний коммит (сообщение остаётся).</summary>
    private async Task AmendAsync()
    {
        if (_busy)
            return;

        if (_repoStatus?.HeadHash is null)
        {
            ShowStatus(T($"{Theme.SymbolError} Amend невозможен: коммитов ещё нет", $"{Theme.SymbolError} Can't amend: no commits yet"), Theme.Error);
            return;
        }

        if (!_files.Entries.Any(r => r.Section == FileSection.Staged))
        {
            ShowStatus(T($"{Theme.SymbolError} Нечего добавлять в коммит: сначала добавь файлы в индекс (space)", $"{Theme.SymbolError} Nothing to add to the commit: stage files first (space)"), Theme.Error);
            return;
        }

        var last = _commits.Items.Count > 0 ? _commits.Items[0] : null;
        var warning = await RewriteWarningAsync(T("Последний коммит будет заменён новым с теми же сообщением и автором. Прежний можно найти в git reflog (или вернуть клавишей z).", "The last commit will be replaced by a new one with the same message and author. The old one is in git reflog (or press z to restore it)."));
        App?.Invoke(() =>
        {
            if (!Confirm(T($"Добавить staged-изменения в коммит «{last?.Subject}»?", $"Add staged changes to commit '{last?.Subject}'?"), GitService.AmendArguments, warning, "Amend"))
                return;

            _ = RunAsync(ct => _git.AmendAsync(ct), T("Изменения добавлены в последний коммит", "Changes added to the last commit"));
        });
    }

    /// <summary>Создаёт коммит, отменяющий выбранный. История не переписывается.</summary>
    private Task RevertSelectedAsync()
    {
        if (_busy || _commits.Selected is not { } commit)
            return Task.CompletedTask;

        if (!Confirm(T($"Откатить коммит {commit.ShortHash} «{commit.Subject}»?", $"Revert commit {commit.ShortHash} '{commit.Subject}'?"), GitService.RevertArguments(commit),
                T("Появится новый коммит с обратными изменениями. Прежний коммит и история остаются.", "A new commit with the opposite changes will appear. The old commit and history stay."), "Revert"))
            return Task.CompletedTask;

        return RunAsync(ct => _git.RevertAsync(commit, ct), T($"Коммит {commit.ShortHash} откачен новым коммитом", $"Commit {commit.ShortHash} reverted with a new commit"));
    }

    /// <summary>Переносит ветку на выбранный коммит. Soft и mixed сохраняют изменения, hard их удаляет.</summary>
    private async Task ResetSelectedAsync(ResetMode mode)
    {
        if (_busy || _commits.Selected is not { } commit)
            return;

        if (_commits.Items.Count > 0 && _commits.Items[0].Hash == commit.Hash && mode != ResetMode.Hard)
        {
            ShowStatus(T($"{Theme.SymbolError} Это уже последний коммит: выбери более старый", $"{Theme.SymbolError} This is already the last commit: pick an older one"), Theme.Error);
            return;
        }

        var normal = mode switch
        {
            ResetMode.Soft => T("Коммиты выше пропадут из ветки, а их изменения окажутся в индексе (staged).", "Commits above will leave the branch, their changes will be staged."),
            ResetMode.Mixed => T("Коммиты выше пропадут из ветки, а их изменения останутся в файлах, но не в индексе.", "Commits above will leave the branch, their changes stay in the files but unstaged."),
            _ => T($"{Theme.SymbolWarning} Коммиты выше пропадут, а все незакоммиченные изменения в файлах будут удалены. Прежний коммит можно найти в git reflog.", $"{Theme.SymbolWarning} Commits above will be lost and all uncommitted changes in files deleted. The old commit is in git reflog."),
        };
        var warning = await RewriteWarningAsync(normal);
        App?.Invoke(() =>
        {
            if (!Confirm(T($"Перенести ветку на {commit.ShortHash} «{commit.Subject}»?", $"Move the branch to {commit.ShortHash} '{commit.Subject}'?"), GitService.ResetArguments(commit, mode), warning, "Reset"))
                return;

            _ = RunAsync(ct => _git.ResetAsync(commit, mode, ct), T($"Ветка перенесена на {commit.ShortHash} (--{mode.ToString().ToLowerInvariant()})", $"Branch moved to {commit.ShortHash} (--{mode.ToString().ToLowerInvariant()})"));
        });
    }

    /// <summary>Копирует вершину выбранной ветки в текущую.</summary>
    private async Task CherryPickBranchTipAsync()
    {
        if (_busy || _branches.Selected is not { } branch)
            return;

        if (branch.IsCurrent)
        {
            ShowStatus(T($"{Theme.SymbolError} Это текущая ветка. Выбери другую ветку, чтобы взять её последний коммит", $"{Theme.SymbolError} This is the current branch. Pick another branch to take its last commit"), Theme.Error);
            return;
        }

        var commit = await _git.FindCommitAsync(branch.Name);
        App?.Invoke(() =>
        {
            if (commit is null)
                ShowStatus(T($"{Theme.SymbolError} В ветке {branch.Name} нет коммитов", $"{Theme.SymbolError} Branch {branch.Name} has no commits"), Theme.Error);
            else
                _ = CherryPickAsync(commit);
        });
    }

    /// <summary>Спрашивает хеш (или имя ветки/тега) коммита и копирует его в текущую ветку.</summary>
    private void OpenCherryPickDialog()
    {
        if (_busy)
            return;

        using var dialog = new InputDialog("Cherry-pick", T("Хеш коммита, имя ветки или тега. Хеш видно в панели коммитов другой ветки", "Commit hash, branch or tag name. Hashes are shown in the commits panel of another branch"),
            T("скопировать", "copy"), text => text.StartsWith('-') ? T("Хеш не может начинаться с -", "A hash can't start with -") : null);
        App!.Run(dialog);
        if (dialog.Result is not { } revision)
            return;

        _ = Task.Run(async () =>
        {
            var commit = await _git.FindCommitAsync(revision);
            App?.Invoke(() =>
            {
                if (commit is null)
                    ShowStatus(T($"{Theme.SymbolError} Коммит «{revision}» не найден. Проверь хеш или имя ветки", $"{Theme.SymbolError} Commit '{revision}' not found. Check the hash or branch name"), Theme.Error);
                else
                    _ = CherryPickAsync(commit);
            });
        });
    }

    private Task CherryPickAsync(Commit commit) =>
        RunAsync(ct => _git.CherryPickAsync(commit.Hash, ct), T($"Коммит {commit.ShortHash} скопирован: {commit.Subject}", $"Commit {commit.ShortHash} copied: {commit.Subject}"));

    private bool Confirm(string question, string[] args, string warning, string confirmText)
    {
        using var dialog = new ConfirmDialog(question, CommandText.Format(args), warning, confirmText);
        App!.Run(dialog);
        return dialog.Result == true;
    }

    /// <summary>
    /// Stage для файла или папки из «Изменений», Untracked и конфликтов, unstage — из Staged.
    /// Для папки действие применяется ко всем её файлам в этом разделе.
    /// </summary>
    private Task ToggleSelectedAsync()
    {
        var (section, files, name) = _files.SelectedRow switch
        {
            FileEntryRow f => (f.Section, (IReadOnlyList<FileChange>)[f.File], f.File.Path),
            DirectoryRow d => (d.Section, d.Files.Select(e => e.File).ToList(), $"{d.Path}/"),
            _ => (default(FileSection), [], ""),
        };
        if (files.Count == 0)
            return Task.CompletedTask;

        return section switch
        {
            FileSection.Staged => RunAsync(ct => _git.UnstageAsync(files, ct), T($"{name} убран из индекса", $"{name} unstaged")),
            FileSection.Conflicts => RunAsync(ct => _git.StageAsync(files, ct), T($"{name}: конфликт отмечен как решённый", $"{name}: conflict marked as resolved")),
            _ => RunAsync(ct => _git.StageAsync(files, ct), T($"{name} добавлен в индекс", $"{name} staged")),
        };
    }

    /// <summary>
    /// Отменяет изменения выбранного файла или папки после подтверждения. Что именно делается, зависит от раздела:
    /// «Изменения» — файл возвращается к версии из индекса, Staged — к HEAD (и индекс, и рабочая копия),
    /// новые файлы удаляются с диска. Конфликты так не сбрасываются.
    /// </summary>
    private Task DiscardSelectedAsync()
    {
        if (_busy)
            return Task.CompletedTask;

        var (section, files, name) = _files.SelectedRow switch
        {
            FileEntryRow f => (f.Section, (IReadOnlyList<FileChange>)[f.File], f.File.Path),
            DirectoryRow d => (d.Section, d.Files.Select(e => e.File).ToList(), $"{d.Path}/ ({FilesWord(d.Files.Count)})"),
            _ => (default(FileSection), [], ""),
        };
        if (files.Count == 0)
            return Task.CompletedTask;

        var isFolder = _files.SelectedRow is DirectoryRow;
        var where = isFolder ? T($"в папке {name}", $"in folder {name}") : T($"в {name}", $"in {name}");

        if (section == FileSection.Conflicts)
        {
            ShowStatus(T($"{Theme.SymbolError} Конфликт так не сбросить: реши его в файле и добавь в индекс (space)", $"{Theme.SymbolError} A conflict can't be discarded this way: resolve it in the file and stage it (space)"), Theme.Error);
            return Task.CompletedTask;
        }

        if (section == FileSection.Staged && _repoStatus?.HeadHash is null)
        {
            ShowStatus(T($"{Theme.SymbolError} Коммитов ещё нет: убери файл из индекса (space), затем удали его как новый (d)", $"{Theme.SymbolError} No commits yet: unstage the file (space), then delete it as new (d)"), Theme.Error);
            return Task.CompletedTask;
        }

        var (args, question, warning, confirmText, action) = section switch
        {
            FileSection.Untracked => (
                GitService.DeleteUntrackedArguments(files),
                isFolder ? T($"Удалить новые файлы в папке {name}?", $"Delete new files in folder {name}?") : T($"Удалить {name}?", $"Delete {name}?"),
                T("Новые файлы ещё не в git: они будут удалены с диска, вернуть их будет нельзя.", "New files are not in git yet: they will be deleted from disk and can't be restored."),
                T("Удалить", "Delete"),
                (Func<CancellationToken, Task<GitResult>>)(ct => _git.DeleteUntrackedAsync(files, ct))),
            FileSection.Staged => (
                GitService.DiscardToHeadArguments(files),
                T($"Сбросить все изменения {where}?", $"Discard all changes {where}?"),
                T("Файлы вернутся к последнему коммиту: и staged, и несохранённые в индексе изменения пропадут. Это нельзя отменить.", "Files return to the last commit: both staged and unstaged changes will be lost. This can't be undone."),
                T("Сбросить", "Discard"),
                ct => _git.DiscardToHeadAsync(files, ct)),
            _ => (
                GitService.DiscardWorktreeArguments(files),
                T($"Сбросить изменения {where}?", $"Discard changes {where}?"),
                T("Изменения, не добавленные в индекс, пропадут. Staged-часть останется. Это нельзя отменить.", "Unstaged changes will be lost. The staged part stays. This can't be undone."),
                T("Сбросить", "Discard"),
                ct => _git.DiscardWorktreeAsync(files, ct)),
        };

        if (!Confirm(question, args, warning, confirmText))
            return Task.CompletedTask;

        var done = section == FileSection.Untracked ? T($"Удалено: {name}", $"Deleted: {name}") : T($"Изменения сброшены: {name}", $"Changes discarded: {name}");
        return RunAsync(action, done);
    }

    /// <summary>Если есть изменения вне индекса — добавляет всё, иначе убирает всё из индекса.</summary>
    private Task ToggleAllAsync()
    {
        var hasUnstaged = _files.Entries.Any(r => r.Section != FileSection.Staged);
        if (hasUnstaged)
            return RunAsync(ct => _git.StageAllAsync(ct), T("Все изменения добавлены в индекс", "All changes staged"));

        return _files.Entries.Count > 0
            ? RunAsync(ct => _git.UnstageAllAsync(ct), T("Все изменения убраны из индекса", "All changes unstaged"))
            : Task.CompletedTask;
    }

    /// <summary>Открывает окно коммита, если в индексе есть изменения.</summary>
    private void OpenCommitDialog()
    {
        if (_busy)
            return;

        var stagedCount = _files.Entries.Count(r => r.Section == FileSection.Staged);
        if (stagedCount == 0)
        {
            ShowStatus(T($"{Theme.SymbolError} Нечего коммитить: добавь файлы в индекс клавишей space или a", $"{Theme.SymbolError} Nothing to commit: stage files with space or a"), Theme.Error);
            return;
        }

        using var dialog = new CommitDialog(stagedCount, async ct => CommitSuggester.Suggest(await _git.GetStagedCodeChangesAsync(ct)));
        App!.Run(dialog);
        if (dialog.Result is not { } message)
            return;

        _ = RunAsync(ct => _git.CommitAsync(message, ct), async _ =>
        {
            var hash = await _git.GetShortHeadAsync();
            return T($"Коммит {hash} создан: {message}", $"Commit {hash} created: {message}");
        });
    }

    private Task<GitResult?> RunAsync(Func<CancellationToken, Task<GitResult>> action, string successMessage, string? progress = null) =>
        RunAsync(action, _ => Task.FromResult(successMessage), progress);

    /// <summary>
    /// Выполняет изменяющую команду, показывает результат и обновляет экран. Одновременно — одна команда.
    /// С <paramref name="progress"/> во время работы крутится спиннер с этим текстом, а esc отменяет команду.
    /// Возвращает результат git или null, если команда не выполнилась (занято другой или отменена).
    /// </summary>
    private async Task<GitResult?> RunAsync(
        Func<CancellationToken, Task<GitResult>> action, Func<GitResult, Task<string>> successMessage, string? progress = null)
    {
        if (_busy)
            return null;

        _busy = true;
        using var operation = new CancellationTokenSource();
        if (progress is not null)
        {
            _operation = operation;
            App?.Invoke(() => StartSpinner(progress));
        }

        try
        {
            var result = await action(operation.Token);
            var success = result.Success ? await successMessage(result) : null;
            await LoadStatusAsync();
            App?.Invoke(() =>
            {
                if (success is not null)
                    ShowStatus($"{Theme.SymbolSuccess} {success}", Theme.Success, autoHide: true);
                else
                    ShowStatus($"{Theme.SymbolError} {GitErrors.Explain(result)}", Theme.Error);
            });
            return result;
        }
        catch (OperationCanceledException)
        {
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolError} Отменено: {progress?.TrimEnd('…')}", $"{Theme.SymbolError} Cancelled: {progress?.TrimEnd('…')}"), Theme.Error));
            await LoadStatusAsync();
            return null;
        }
        finally
        {
            _operation = null;
            App?.Invoke(StopSpinner);
            _busy = false;
            RunPendingRefresh();
        }
    }

    /// <summary>Скачивает новые коммиты и сообщает, есть ли что забрать.</summary>
    private Task FetchAsync() =>
        RunAsync(ct => _git.FetchAsync(ct), async _ =>
        {
            var status = await _git.GetStatusAsync();
            return status.Behind > 0
                ? T($"На сервере {PluralRu(status.Behind, "новый коммит", "новых коммита", "новых коммитов")}. Нажми p, чтобы забрать", $"The server has {PluralEn(status.Behind, "new commit", "new commits")}. Press p to get them")
                : T("Fetch выполнен, новых коммитов нет", "Fetch done, no new commits");
        }, T("Получение изменений с сервера…", "Fetching changes from the server…"));

    private Task PullAsync()
    {
        var upstream = _repoStatus?.Upstream ?? "upstream";
        return RunAsync(ct => _git.PullAsync(ct), result => Task.FromResult(
            result.StdOut.Contains("Already up to date") ? T($"Уже актуально: в {upstream} нет новых коммитов", $"Up to date: {upstream} has no new commits")
            : result.StdOut.Contains("Fast-forward") ? T($"Pull из {upstream} выполнен (fast-forward)", $"Pulled from {upstream} (fast-forward)")
            : T($"Pull из {upstream} выполнен, изменения слиты", $"Pulled from {upstream}, changes merged")), T($"Pull из {upstream}…", $"Pulling from {upstream}…"));
    }

    /// <summary>
    /// Push текущей ветки. Если upstream нет, первое нажатие объясняет, что будет сделано,
    /// второе — публикует ветку командой push -u.
    /// </summary>
    private async Task PushAsync()
    {
        if (_busy || _repoStatus is not { } status)
            return;

        if (status.IsDetached || status.Branch is not { } branch)
        {
            ShowStatus(T($"{Theme.SymbolError} HEAD не на ветке (detached). Создай ветку (n в панели веток) и сделай push", $"{Theme.SymbolError} HEAD is not on a branch (detached). Create a branch (n in the branches panel) and push"), Theme.Error);
            return;
        }

        if (status.Upstream is { } upstream)
        {
            _pushUpstreamOffer = null;
            var count = status.Ahead > 0 ? Plural(status.Ahead, "коммит", "коммита", "коммитов", "commit", "commits") + " " : "";
            await RunAsync(ct => _git.PushAsync(ct),
                async _ => T($"Отправлено {count}в {upstream}", $"Pushed {count}to {upstream}") + await PullRequestHintAsync(branch),
                T($"Отправка в {upstream}…", $"Pushing to {upstream}…"));
            return;
        }

        IReadOnlyList<string> remotes;
        try
        {
            remotes = await _git.GetRemotesAsync();
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}", Theme.Error));
            return;
        }

        if (remotes.Count == 0)
        {
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolError} Remote не настроен. Добавь его: git remote add origin <адрес>", $"{Theme.SymbolError} No remote configured. Add one: git remote add origin <url>"), Theme.Error));
            return;
        }

        var remote = remotes[0];
        var command = CommandText.Format(GitService.PushSetUpstreamArguments(remote, branch));
        if (_pushUpstreamOffer != branch)
        {
            _pushUpstreamOffer = branch;
            App?.Invoke(() => ShowStatus(
                T($"{Theme.SymbolWarning} Нет upstream для {branch}. Нажми P ещё раз, чтобы выполнить {command}", $"{Theme.SymbolWarning} No upstream for {branch}. Press P again to run {command}"), Theme.Warning));
            return;
        }

        _pushUpstreamOffer = null;
        await RunAsync(ct => _git.PushSetUpstreamAsync(remote, branch, ct),
            T($"Ветка {branch} опубликована в {remote}, upstream настроен", $"Branch {branch} published to {remote}, upstream set"), T($"Публикация {branch} в {remote}…", $"Publishing {branch} to {remote}…"));
    }

    /// <summary>
    /// «· N — создать PR» после push ветки, которая не основная (по origin/HEAD, без сети). Только при входе в GitHub.
    /// </summary>
    private async Task<string> PullRequestHintAsync(string branch)
    {
        if (!_auth.IsAuthorized)
            return "";

        var main = await _git.GetRemoteDefaultBranchAsync();
        return main is null || main == branch ? "" : T($" {Theme.SymbolSeparator} N — создать PR", $" {Theme.SymbolSeparator} N creates a PR");
    }

    /// <summary>Репозиторий проекта на GitHub или null с объяснением в строке статуса (gh не установлен, не вошёл, remote не GitHub).</summary>
    private GitHubRepo? ResolveGitHubRepo()
    {
        if (!_auth.GhInstalled)
        {
            ShowStatus(T($"{Theme.SymbolError} gh не найден. Установи: {GhInstallHint.Command()}",
                $"{Theme.SymbolError} gh not found. Install it: {GhInstallHint.Command()}"), Theme.Error);
            return null;
        }

        if (!_auth.IsAuthorized)
        {
            ShowStatus(T($"{Theme.SymbolError} Сначала войди в GitHub: ctrl+g", $"{Theme.SymbolError} Sign in to GitHub first: ctrl+g"), Theme.Error);
            return null;
        }

        if (_gitHubRepo is null)
            ShowStatus(_remoteUrl is null
                ? T($"{Theme.SymbolError} У репозитория нет remote. Добавь: git remote add origin <адрес>", $"{Theme.SymbolError} The repository has no remote. Add one: git remote add origin <url>")
                : T($"{Theme.SymbolError} Remote не на GitHub: {_remoteUrl}", $"{Theme.SymbolError} The remote is not on GitHub: {_remoteUrl}"), Theme.Error);
        return _gitHubRepo;
    }

    private GitHubService CreateGitHubService() => new(new GhRunner(), _git.RepoRoot, _commandLog);

    /// <summary>
    /// Статус CI коммита HEAD в шапке. Вызывается при каждом обновлении экрана, но в сеть идёт, только если коммит
    /// новый или прошёл интервал. Ошибка сети не показывается: статус CI — подсказка, а не действие.
    /// </summary>
    private async Task UpdateCiAsync()
    {
        var sha = _repoStatus?.HeadHash;
        if (!_auth.IsAuthorized || _gitHubRepo is not { } repo || sha is null)
        {
            _ciRuns = [];
            _header.SetCi(CheckState.None);
            return;
        }

        if (sha != _ciSha)
            _header.SetCi(CheckState.None);
        var interval = WorkflowRunParser.Summarize(_ciRuns) == CheckState.Pending ? CiPendingInterval : CiIdleInterval;
        if (_ciLoading || (sha == _ciSha && DateTimeOffset.Now - _ciCheckedAt < interval))
            return;

        _ciLoading = true;
        IReadOnlyList<WorkflowRun> runs;
        try
        {
            runs = await new GitHubService(new GhRunner(), _git.RepoRoot).GetRunsAsync(repo, sha);
        }
        catch (Exception ex) when (ex is GitCommandException or System.Text.Json.JsonException)
        {
            runs = [];
        }
        finally
        {
            _ciLoading = false;
        }

        App?.Invoke(() =>
        {
            if (_repoStatus?.HeadHash != sha)
                return;

            _ciSha = sha;
            _ciCheckedAt = DateTimeOffset.Now;
            _ciRuns = runs;
            var state = WorkflowRunParser.Summarize(runs);
            _header.SetCi(state);
            UpdateHints();
            // Пока CI идёт, статус обновляется сам, даже если в репозитории ничего не меняется.
            if (state == CheckState.Pending && _ciTimer is null)
                _ciTimer = App?.AddTimeout(CiPendingInterval, () =>
                {
                    _ciTimer = null;
                    _ = UpdateCiAsync();
                    return false;
                });
        });
    }

    /// <summary>
    /// Окно Actions: запуски текущей ветки, выбран запуск HEAD. Открывается прямо из обработчика клавиши (не из Invoke):
    /// иначе окно не получило бы свои загрузки.
    /// </summary>
    private void OpenActions()
    {
        if (_busy || ResolveGitHubRepo() is not { } repo)
            return;

        using var window = new ActionsWindow(CreateGitHubService(), repo, _repoStatus?.Branch, _repoStatus?.HeadHash);
        App!.Run(window);
        _ciCheckedAt = default;
        _ = UpdateCiAsync();
    }

    /// <summary>
    /// Окно GitHub. Открывается прямо из обработчика клавиши, а не из App.Invoke: внутри Invoke окно не получило бы
    /// результаты своих фоновых загрузок. Если в нём сделали checkout — экран обновляется, «n» — открывается окно нового PR.
    /// </summary>
    private void OpenGitHub(GitHubTab tab)
    {
        if (_busy || ResolveGitHubRepo() is not { } repo)
            return;

        var recent = _config.Load().RecentRepos ?? [];
        using var window = new GitHubWindow(CreateGitHubService(), repo, tab,
            new LocalProjects(new GitRunner(), [_git.RepoRoot, .. recent.Where(p => p != _git.RepoRoot)]), _git.RepoRoot);
        App!.Run(window);
        switch (window.Result)
        {
            case GitHubAction.OpenRepository:
                OpenRepoPath = window.OpenPath;
                Result = MainResult.OpenRepo;
                RequestStop();
                break;
            case GitHubAction.Refresh:
                _ = RefreshAsync();
                break;
            case GitHubAction.CreatePullRequest:
                _ = CreatePullRequestAsync();
                break;
        }
    }

    /// <summary>
    /// Новый PR из текущей ветки в основную. Ветка должна быть отправлена: иначе подсказка сделать push.
    /// Заголовок — из единственного коммита или имени ветки, описание — список коммитов ветки.
    /// </summary>
    private async Task CreatePullRequestAsync()
    {
        if (_busy || _repoStatus is not { Branch: { } branch } status)
            return;

        if (status.Upstream is null || status.Ahead > 0)
        {
            ShowStatus(T($"{Theme.SymbolError} Сначала отправь ветку на GitHub: P", $"{Theme.SymbolError} Push the branch to GitHub first: P"), Theme.Error);
            return;
        }

        if (ResolveGitHubRepo() is not { } repo)
            return;

        var github = CreateGitHubService();
        App?.Invoke(() => ShowStatus($"{Theme.SpinnerFrames[0]} {T("Проверяю ветку на GitHub…", "Checking the branch on GitHub…")}", Theme.Muted));
        string main;
        PullRequest? existing;
        try
        {
            main = await github.GetDefaultBranchAsync(repo);
            existing = await github.FindForBranchAsync(repo, branch);
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}", Theme.Error));
            return;
        }

        if (branch == main)
        {
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolError} Ты на основной ветке {main}: создай ветку для изменений (n в ветках)",
                $"{Theme.SymbolError} You are on the base branch {main}: create a branch for the changes (n in branches)"), Theme.Error));
            return;
        }

        if (existing is not null)
        {
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolWarning} PR #{existing.Number} из {branch} уже открыт: g — посмотреть",
                $"{Theme.SymbolWarning} PR #{existing.Number} from {branch} is already open: g to view it"), Theme.Warning));
            return;
        }

        var remote = status.Upstream.Split('/')[0];
        var subjects = await _git.GetCommitSubjectsAsync($"{remote}/{main}..HEAD");
        var title = subjects.Count == 1 ? subjects[0] : TitleFromBranch(branch);
        var body = subjects.Count > 1 ? string.Join('\n', subjects.Select(s => $"- {s}")) : "";
        var commits = Plural(subjects.Count, "коммит", "коммита", "коммитов", "commit", "commits");

        App?.Invoke(() =>
        {
            ShowStatus("", Theme.Muted);
            using var dialog = new PullRequestDialog(repo.NameWithOwner, $"{Theme.SymbolBranch} {branch} → {main} {Theme.SymbolSeparator} {commits}", title, body);
            App!.Run(dialog);
            if (dialog.Result is not { } pr)
                return;

            _ = RunAsync(ct => github.CreateAsync(repo, branch, main, pr.Title, pr.Body, pr.Draft, ct), result =>
            {
                var url = result.StdOut.Trim().Split('\n').LastOrDefault() ?? "";
                return Task.FromResult(T($"PR создан: {url} (g — открыть список)", $"PR created: {url} (g opens the list)"));
            }, T("Создание PR…", "Creating the PR…"));
        });
    }

    /// <summary>«feature/hourly-forecast» → «hourly forecast».</summary>
    public static string TitleFromBranch(string branch) =>
        branch[(branch.LastIndexOf('/') + 1)..].Replace('-', ' ').Replace('_', ' ');

    /// <summary>Спиннер в строке статуса: «⠹ Отправка в origin/main…  esc отмена».</summary>
    private void StartSpinner(string text)
    {
        ShowStatus("", Theme.Muted);
        var frame = 0;
        void Draw() => _status.Text = T($"{Theme.SpinnerFrames[frame % Theme.SpinnerFrames.Length]} {text}  esc отмена", $"{Theme.SpinnerFrames[frame % Theme.SpinnerFrames.Length]} {text}  esc cancel");
        Draw();
        _spinnerTimeout = App?.AddTimeout(TimeSpan.FromMilliseconds(80), () =>
        {
            frame++;
            Draw();
            return true;
        });
    }

    private void StopSpinner()
    {
        if (_spinnerTimeout is null)
            return;

        App?.RemoveTimeout(_spinnerTimeout);
        _spinnerTimeout = null;
    }

    /// <summary>
    /// Перечитывает статус. silent — автообновление: без сообщения «Обновление…».
    /// Если сейчас выполняется другая команда, обновление откладывается до её завершения.
    /// </summary>
    private async Task RefreshAsync(bool silent = false)
    {
        if (_busy)
        {
            _refreshPending = true;
            return;
        }

        _busy = true;
        if (!silent)
            App?.Invoke(() => ShowStatus(T($"{Theme.SpinnerFrames[0]} Обновление…", $"{Theme.SpinnerFrames[0]} Refreshing…"), Theme.Muted));
        try
        {
            if (await LoadStatusAsync() && !silent)
                App?.Invoke(() => ShowStatus("", Theme.Muted));
        }
        finally
        {
            _busy = false;
            RunPendingRefresh();
        }
    }

    private void RunPendingRefresh()
    {
        if (!_refreshPending)
            return;

        _refreshPending = false;
        App?.Invoke(() => _ = RefreshAsync(silent: true));
    }

    /// <summary>Перечитывает статус и лог, обновляет шапку и панели. Возвращает false при ошибке.</summary>
    private async Task<bool> LoadStatusAsync()
    {
        try
        {
            var statusTask = _git.GetStatusAsync();
            var branchesTask = _git.GetBranchesAsync();
            var logTask = _git.GetLogAsync(_logLimit);
            var stashTask = _git.GetStashesAsync();
            var operationTask = _git.GetOperationAsync();
            var remoteTask = _git.GetRemoteUrlAsync();
            var targetTask = Task.Run(() => DotnetTarget.Find(_git.RepoRoot));
            var worktreesTask = _git.GetWorktreesAsync();
            var status = await statusTask;
            var branches = await branchesTask;
            var log = await logTask;
            var stashes = await stashTask;
            var operation = await operationTask;
            var remoteUrl = await remoteTask;
            var dotnetTarget = await targetTask;
            var worktrees = await worktreesTask;
            App?.Invoke(() =>
            {
                _repoStatus = status;
                _repoOperation = operation;
                _remoteUrl = remoteUrl;
                _dotnetTarget = dotnetTarget;
                _branches.InWorktrees = worktrees.Where(w => w.Branch is not null && w.Path != _git.RepoRoot).ToDictionary(w => w.Branch!, w => w.Path);
                _gitHubRepo = remoteUrl is null ? null : GitHubRepo.FromRemoteUrl(remoteUrl);
                _header.SetStatus(status, operation);
                _ = UpdateCiAsync();
                _ = UpdateMergedCopiesAsync();
                _files.SetStatus(status);
                _branches.SetItems(branches);
                _commits.SetItems(log);
                SetStashes(stashes);
                UpdateTitles();
                UpdateHints();
                // Лог ветки справа перечитывается: в ней могли появиться коммиты.
                if (_branches.HasFocus)
                    _ = LoadDiffAsync();
                // Панель файлов перезагружает diff только в своём фокусе, в построчном stage — здесь.
                if (_staging is not null)
                    _ = LoadDiffAsync();
            });
            return true;
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
            return false;
        }
    }

    /// <summary>
    /// Показывает справа выбранный коммит, если в фокусе панель коммитов, иначе — diff выбранного файла.
    /// Предыдущая незавершённая загрузка отменяется.
    /// </summary>
    private Task LoadDiffAsync()
    {
        _diffLoading?.Cancel();
        if (_conflict is not null)
            return Task.CompletedTask;
        // Справа лог ветки, пока в фокусе ветки, иначе diff.
        var branchLog = _staging is null && _branches.HasFocus;
        _branchLog.Visible = branchLog;
        _diff.Visible = !branchLog;
        if (_staging is not null)
            return LoadStagingDiffAsync();
        if (branchLog)
            return LoadBranchLogAsync();
        if (_stash.HasFocus)
            return LoadStashDiffAsync();
        return _commits.HasFocus ? LoadCommitDiffAsync() : LoadFileDiffAsync();
    }

    /// <summary>
    /// git log --graph выбранной ветки справа, как в lazygit, но в цветах приложения — без checkout.
    /// Видно, где HEAD и чем ветка отличается от текущей. В заголовке upstream и ↑↓.
    /// </summary>
    private async Task LoadBranchLogAsync()
    {
        _diffKey = null;
        if (_branches.Selected is not { } branch)
        {
            _branchLog.Visible = false;
            _diff.Visible = true;
            _diffTitle.Text = T(" Ветка ", " Branch ");
            _diff.SetMessage(T("Веток пока нет: сделай первый коммит", "No branches yet: make the first commit"));
            return;
        }

        var upstream = branch switch
        {
            { IsUpstreamGone: true } => T($"upstream {branch.Upstream} удалён на сервере", $"upstream {branch.Upstream} deleted on the server"),
            { Upstream: { } name } => $"{name} {Theme.SymbolAhead}{branch.Ahead} {Theme.SymbolBehind}{branch.Behind}",
            _ => T("без upstream", "no upstream"),
        };
        _diffTitle.Text = $" {Theme.SymbolBranch} {branch.Name} {Theme.SymbolSeparator} {upstream} ";

        // Без using: следующая загрузка отменяет этот токен, а отмена освобождённого бросила бы исключение.
        var loading = new CancellationTokenSource();
        _diffLoading = loading;
        try
        {
            var log = await _git.GetBranchGraphLogAsync(branch, ct: loading.Token);
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _branchLog.SetLines(log, keepScroll: _branchLogFor == branch.Name);
                _branchLogFor = branch.Name;
            });
        }
        catch (OperationCanceledException)
        {
            // Выбрали другую ветку — загрузится она.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    /// <summary>ctrl+d / ctrl+u: на пол-экрана в diff или в логе ветки — что сейчас справа.</summary>
    private void ScrollRight(int direction)
    {
        View view = _conflictPanel.Visible ? _conflictPanel : _branchLog.Visible ? _branchLog : _diff;
        var step = direction * Math.Max(1, view.Viewport.Height / 2);
        if (_conflictPanel.Visible)
            _conflictPanel.ScrollBy(step);
        else if (_branchLog.Visible)
            _branchLog.ScrollBy(step);
        else
            _diff.ScrollBy(step);
    }

    /// <summary>
    /// Счётчики в заголовках панелей: «[1] Файлы · 5», «[2] Ветки · 1/3», «[3] Коммиты · 4/200+».
    /// «+» — лог показан не целиком: коммитов больше, чем загружено.
    /// </summary>
    private void UpdateTitles()
    {
        static string Position<TItem>(ListPanel<TItem> panel, int limit = int.MaxValue) where TItem : class =>
            panel.Items.Count == 0 ? ""
            : $" {Theme.SymbolSeparator} {panel.SelectedIndex + 1}/{panel.Items.Count}{(panel.Items.Count >= limit ? "+" : "")}";

        var files = _files.Entries.Select(e => e.File.Path).Distinct().Count();
        _filesTitle.Text = $" {T("[1] Файлы", "[1] Files")}{(files > 0 ? $" {Theme.SymbolSeparator} {files}" : "")} ";
        _branchesTitle.Text = $" {T("[2] Ветки", "[2] Branches")}{Position(_branches)} ";
        _commitsTitle.Text = $" {T("[3] Коммиты", "[3] Commits")}{Position(_commits, _logLimit)} ";
        _stashTitle.Text = $" [4] Stash{Position(_stash)} ";
    }

    private Task LoadCommitDiffAsync()
    {
        if (_commits.Selected is not { } commit)
        {
            _diffKey = null;
            _diffTitle.Text = T(" Коммит ", " Commit ");
            _diff.SetMessage(T("Коммитов пока нет", "No commits yet"));
            return Task.CompletedTask;
        }

        var details = _codeDetails;
        return ShowDiffAsync((commit.Hash, details), T($" Коммит · {commit.ShortHash} ", $" Commit · {commit.ShortHash} "), async ct =>
        {
            var diff = await _git.GetCommitDiffAsync(commit, ct);
            var prepared = await Task.Run(() => DiffPanel.PrepareCommit(commit, diff, DateTimeOffset.Now), ct);
            return details
                ? DiffPanel.WithCodeChanges(prepared, await _git.GetCodeChangesAsync(commit.Parents.FirstOrDefault() ?? GitService.EmptyTree, commit.Hash, ct))
                : prepared;
        });
    }

    private Task LoadStashDiffAsync()
    {
        if (_stash.Selected is not { } stash)
            return Task.CompletedTask;

        var details = _codeDetails;
        return ShowDiffAsync((stash.Hash, details), $" Stash · {stash.Ref} ", async ct =>
        {
            var diff = await _git.GetStashDiffAsync(stash, ct);
            var asCommit = new Commit(stash.Hash, stash.AuthorName, stash.AuthorEmail, stash.Date, stash.Message);
            var prepared = await Task.Run(() => DiffPanel.PrepareCommit(asCommit, diff, DateTimeOffset.Now), ct);
            // Stash — коммит над состоянием, от которого спрятали: его первый родитель.
            return details ? DiffPanel.WithCodeChanges(prepared, await _git.GetCodeChangesAsync($"{stash.Hash}^1", stash.Hash, ct)) : prepared;
        });
    }

    private Task LoadFileDiffAsync()
    {
        if (_files.SelectedDirectory is { } directory)
        {
            _diffKey = null;
            _diffTitle.Text = $" Diff · {directory.Path}/ ";
            var files = directory.Files.Count;
            var action = directory.Collapsed ? T("развернуть", "expand") : T("свернуть", "collapse");
            _diff.SetMessage(T($"Папка: {FilesWord(files)}. space — stage всей папки, enter — {action}", $"Folder: {FilesWord(files)}. space stages the whole folder, enter: {action}"));
            return Task.CompletedTask;
        }

        if (_files.SelectedFile is not { } row)
        {
            _diffKey = null;
            _diffTitle.Text = " Diff ";
            _diff.SetMessage("");
            return Task.CompletedTask;
        }

        var staged = row.Section == FileSection.Staged;
        var tag = staged ? " · staged" : row.File.IsUntracked ? T(" · новый файл", " · new file") : "";
        var details = _codeDetails;
        return ShowDiffAsync((staged, row.File.Path, details), $" Diff · {row.File.Path}{tag} ", async ct =>
        {
            var lines = await _git.GetDiffAsync(row.File, staged, ct);
            var prepared = await Task.Run(() => DiffPanel.Prepare(lines, row.File.Path), ct);
            if (!details || !Highlighting.DiffHighlighter.IsCSharp(row.File.Path))
                return prepared;

            var changes = await _git.GetCodeChangesAsync(row.File, staged, ct);
            return DiffPanel.WithCodeChanges(prepared, changes.Count == 0 ? [] : [(row.File.Path, changes)]);
        });
    }

    /// <summary>
    /// Режим решения конфликта по блокам: файл справа целиком, ↑↓ — к блоку, o / t / b — оставить сторону.
    /// Нельзя (не UTF-8, нет маркеров, маркеры не сходятся) — объяснение и подсказка решить файл целиком.
    /// </summary>
    private async Task StartConflictAsync()
    {
        if (_busy || _files.SelectedRow is not FileEntryRow { Section: FileSection.Conflicts } row)
            return;

        var (file, problem) = await _git.ReadConflictFileAsync(row.File.Path);
        App?.Invoke(() =>
        {
            if (file is null)
            {
                ShowStatus($"{Theme.SymbolError} {problem}", Theme.Error);
                return;
            }

            StopLineStaging();
            _conflict = file;
            _conflictPanel.Reset();
            ShowConflict(file);
            _conflictPanel.Visible = true;
            _diff.Visible = false;
            _branchLog.Visible = false;
            _conflictPanel.SetFocus();
            UpdateHints();
        });
    }

    private void ShowConflict(ConflictFile file)
    {
        _conflictPanel.SetFile(file);
        _diffTitle.Text = T($" Конфликт {Theme.SymbolSeparator} {file.Path} {Theme.SymbolSeparator} {Plural(file.Blocks, "блок", "блока", "блоков", "block", "blocks")} ",
            $" Conflict {Theme.SymbolSeparator} {file.Path} {Theme.SymbolSeparator} {Plural(file.Blocks, "блок", "блока", "блоков", "block", "blocks")} ");
    }

    private void StopConflict()
    {
        if (_conflict is null)
            return;

        _conflict = null;
        _conflictPanel.Visible = false;
        _diff.Visible = true;
        UpdateHints();
        _ = LoadDiffAsync();
    }

    /// <summary>
    /// Оставляет выбранную сторону в текущем блоке и сразу записывает файл. Решён последний блок — файл добавляется
    /// в индекс (конфликт отмечен решённым) и режим заканчивается.
    /// </summary>
    private async Task ResolveBlockAsync(ConflictChoice choice)
    {
        if (_busy || _conflict is not { } file)
            return;

        var parts = ConflictResolver.Resolve(file.Parts, _conflictPanel.CurrentBlock, choice);
        try
        {
            await _git.WriteConflictFileAsync(file, parts);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolError} Не удалось записать {file.Path}: {ex.Message}",
                $"{Theme.SymbolError} Couldn't write {file.Path}: {ex.Message}"), Theme.Error));
            return;
        }

        var left = ConflictResolver.CountBlocks(parts);
        App?.Invoke(() =>
        {
            if (left > 0)
            {
                _conflict = file with { Parts = parts };
                ShowConflict(_conflict);
                ShowStatus(T($"{Theme.SymbolSuccess} Блок решён, осталось: {left}", $"{Theme.SymbolSuccess} Block resolved, left: {left}"),
                    Theme.Success, autoHide: true);
                return;
            }

            var change = _repoStatus?.Files.FirstOrDefault(f => f.Path == file.Path);
            _files.SetFocus();
            if (change is not null)
                _ = RunAsync(ct => _git.StageAsync(change, ct),
                    T($"{file.Path}: все блоки решены, файл в индексе. Вернуть конфликт: git checkout -m -- {file.Path}",
                        $"{file.Path}: all blocks resolved, the file is staged. Restore the conflict: git checkout -m -- {file.Path}"));
        });
    }

    /// <summary>Возвращает файл к состоянию сразу после merge: все блоки снова с маркерами.</summary>
    private async Task RestoreConflictAsync()
    {
        if (_busy || _conflict is not { } file)
            return;

        if (!Confirm(T($"Вернуть конфликт в {file.Path}?", $"Restore the conflict in {file.Path}?"), GitService.RestoreConflictArguments(file.Path),
                T("Все блоки снова станут конфликтными, решения и ручные правки в файле пропадут.",
                    "All blocks become conflicts again; resolutions and manual edits in the file are lost."), T("Вернуть", "Restore")))
            return;

        var result = await RunAsync(ct => _git.RestoreConflictAsync(file.Path, ct), T($"Конфликт в {file.Path} возвращён", $"Conflict in {file.Path} restored"));
        if (result is not { Success: true })
            return;

        var (restored, _) = await _git.ReadConflictFileAsync(file.Path);
        App?.Invoke(() =>
        {
            if (restored is null || _conflict is null)
                return;

            _conflict = restored;
            _conflictPanel.Reset();
            ShowConflict(restored);
        });
    }

    /// <summary>Включает построчный stage выбранного файла: фокус переходит в diff.</summary>
    private void StartLineStaging()
    {
        if (_files.SelectedFile is not { } row)
            return;

        if (row.Section == FileSection.Staged && row.File.OriginalPath is not null)
        {
            ShowStatus(T($"{Theme.SymbolError} Переименованный файл не убрать из индекса по строкам: unstage его целиком (space)", $"{Theme.SymbolError} A renamed file can't be unstaged by lines: unstage it whole (space)"), Theme.Error);
            return;
        }

        _staging = new StagingTarget(row.File.Path, row.Section == FileSection.Staged);
        _diff.StartStaging();
        UpdateHints();
        _ = LoadDiffAsync();
    }

    private void StopLineStaging()
    {
        if (_staging is null)
            return;

        _staging = null;
        _diff.StopStaging();
        UpdateHints();
    }

    /// <summary>
    /// Diff файла в построчном stage. Файл берётся из последнего статуса по пути, а не из выбора в панели:
    /// после частичного stage новый файл становится отслеживаемым, а выбор может уйти на другую строку.
    /// Если в нужной стороне изменений не осталось, режим выключается.
    /// </summary>
    private Task LoadStagingDiffAsync()
    {
        var target = _staging!;
        var file = _repoStatus?.Files.FirstOrDefault(f => f.Path == target.Path
            && (target.Staged ? f.HasStagedChanges : f.HasUnstagedChanges));
        if (file is null)
        {
            var where = target.Staged ? T("убраны из индекса", "unstaged") : T("в индексе", "staged");
            StopLineStaging();
            _files.SetFocus();
            ShowStatus(T($"{Theme.SymbolSuccess} Все изменения {target.Path} {where}", $"{Theme.SymbolSuccess} All changes of {target.Path} {where}"), Theme.Success, autoHide: true);
            return Task.CompletedTask;
        }

        var title = target.Staged ? T($" Unstage по строкам · {file.Path} · staged ", $" Unstage by lines · {file.Path} · staged ") : T($" Stage по строкам · {file.Path} ", $" Stage by lines · {file.Path} ");
        return ShowDiffAsync((target.Staged, file.Path), title, async ct =>
        {
            var lines = await _git.GetDiffAsync(file, target.Staged, ct);
            return await Task.Run(() => DiffPanel.Prepare(lines, file.Path), ct);
        });
    }

    /// <summary>Есть ли у файла изменения в другой стороне: в индексе для рабочей копии и наоборот.</summary>
    private bool HasOtherSide(StagingTarget target) =>
        _repoStatus?.Files.FirstOrDefault(f => f.Path == target.Path) is { } file
        && (target.Staged ? file.HasUnstagedChanges : file.HasStagedChanges && file.OriginalPath is null);

    /// <summary>Переходит к другой части того же файла: staged ↔ изменения рабочей копии.</summary>
    private void SwitchStagingSide()
    {
        if (_staging is not { } target || !HasOtherSide(target))
            return;

        _staging = target with { Staged = !target.Staged };
        UpdateHints();
        _ = LoadDiffAsync();
    }

    /// <summary>Выбранные строки как индексы в исходном diff или null с объяснением в строке статуса.</summary>
    private HashSet<int>? SelectedSourceLines(IReadOnlyList<DiffLine> source, IReadOnlyList<DiffLine> changes, string action)
    {
        var chosen = changes.ToHashSet(ReferenceEqualityComparer.Instance);
        if (chosen.Count == 0)
        {
            ShowStatus(T($"{Theme.SymbolError} Встань на строку с + или -", $"{Theme.SymbolError} Move to a line with + or -"), Theme.Error);
            return null;
        }

        if (source.Any(l => l.Kind == DiffLineKind.Binary))
        {
            ShowStatus(T($"{Theme.SymbolError} Бинарный файл по строкам не {action}: используй панель файлов", $"{Theme.SymbolError} A binary file can't be {action} by lines: use the files panel"), Theme.Error);
            return null;
        }

        // Git отдал не UTF-8 (например, windows-1251): записанный обратно патч испортил бы файл.
        if (source.Any(l => l.Text.Contains('\uFFFD')))
        {
            ShowStatus(T($"{Theme.SymbolError} Файл не в UTF-8, по строкам его не {action} безопасно: используй панель файлов", $"{Theme.SymbolError} The file is not UTF-8, it can't be {action} by lines safely: use the files panel"), Theme.Error);
            return null;
        }

        return Enumerable.Range(0, source.Count).Where(i => chosen.Contains(source[i])).ToHashSet();
    }

    /// <summary>Сбрасывает выбранные строки в рабочей копии (с подтверждением). Только для не staged изменений.</summary>
    private Task DiscardLinesAsync()
    {
        if (_busy || _staging is not { Staged: false } target || _diff.Source is not { } source)
            return Task.CompletedTask;

        if (SelectedSourceLines(source, _diff.SelectedChanges, T("сбросить", "discarded")) is not { } selected)
            return Task.CompletedTask;

        // Diff «индекс → рабочая копия» откатывается в рабочей копии: патч строится как для unstage.
        if (PatchBuilder.Build(source, selected, reverse: true) is not { } patch)
            return Task.CompletedTask;

        var count = Plural(selected.Count, "строку", "строки", "строк", "line", "lines");
        var done = Plural(selected.Count, "строка", "строки", "строк", "line", "lines");
        if (!Confirm(T($"Сбросить {count} в {target.Path}?", $"Discard {count} in {target.Path}?"), GitService.DiscardLinesArguments,
                T($"{Theme.SymbolWarning} Изменения пропадут из файла безвозвратно: их нет ни в коммитах, ни в reflog.", $"{Theme.SymbolWarning} The changes will be lost for good: they are in no commit and not in reflog."), T("Сбросить", "Discard")))
            return Task.CompletedTask;

        return RunAsync(ct => _git.DiscardLinesAsync(patch, ct), T($"Сброшено в рабочей копии: {done}", $"Discarded in the working tree: {done}"));
    }

    /// <summary>Stage (или unstage) выбранных строк либо всего фрагмента под курсором.</summary>
    private Task ApplyLinesAsync(bool hunk)
    {
        if (_busy || _staging is not { } target || _diff.Source is not { } source)
            return Task.CompletedTask;

        var changes = hunk ? _diff.CurrentHunkChanges : _diff.SelectedChanges;
        if (SelectedSourceLines(source, changes, target.Staged ? T("убрать из индекса", "unstaged") : T("добавить", "staged")) is not { } selected)
            return Task.CompletedTask;

        if (PatchBuilder.Build(source, selected, target.Staged) is not { } patch)
            return Task.CompletedTask;

        var count = Plural(selected.Count, "строка", "строки", "строк", "line", "lines");
        var done = target.Staged ? T($"Убрано из индекса: {count}", $"Unstaged: {count}") : T($"Добавлено в индекс: {count}", $"Staged: {count}");
        return RunAsync(ct => _git.ApplyToIndexAsync(patch, target.Staged, ct), done);
    }

    /// <summary>Загружает и показывает diff. Для того же <paramref name="key"/>, что и сейчас, прокрутка сохраняется.</summary>
    private async Task ShowDiffAsync(object key, string title, Func<CancellationToken, Task<PreparedDiff>> load)
    {
        var same = Equals(_diffKey, key);
        _diffKey = key;
        _diffTitle.Text = title;

        var loading = new CancellationTokenSource();
        _diffLoading = loading;
        try
        {
            var prepared = await load(loading.Token);
            App?.Invoke(() =>
            {
                if (!loading.IsCancellationRequested)
                    _diff.SetDiff(prepared, keepScroll: same);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбрано другое.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _diff.SetMessage($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}"));
        }
    }

    private static string FilesWord(int count) => Plural(count, "файл", "файла", "файлов", "file", "files");

    /// <summary>Показывает сообщение над подсказками. С autoHide сообщение исчезает через 3 секунды.</summary>
    private void ShowStatus(string text, Color color, bool autoHide = false)
    {
        StopSpinner();
        if (_statusTimeout is not null)
        {
            App?.RemoveTimeout(_statusTimeout);
            _statusTimeout = null;
        }

        _status.Text = text;
        _status.SetScheme(Theme.Text(color));

        if (autoHide && App is not null)
        {
            _statusTimeout = App.AddTimeout(TimeSpan.FromSeconds(3), () =>
            {
                _status.Text = "";
                _statusTimeout = null;
                return false;
            });
        }
    }
}
