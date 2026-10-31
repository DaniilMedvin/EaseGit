using LazyGit.Auth;
using LazyGit.Commands;
using LazyGit.Config;
using LazyGit.Git;
using LazyGit.Models;
using LazyGit.UI.Dialogs;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI;

public enum MainResult
{
    Quit,
    Login,
}

/// <summary>
/// Главный экран репозитория: шапка, слева панели файлов, веток и коммитов, справа diff.
/// 1 / 2 / 3 и tab — переход между панелями.
/// В файлах: space — stage/unstage файла или папки, a — всех файлов, d — сбросить изменения.
/// В ветках: space — checkout, n — новая ветка, M — merge в текущую, D — удалить.
/// На конфликтном файле: o — оставить нашу версию, t — их, b — обе. A — прервать merge / cherry-pick / rebase.
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
    private readonly CommandLogPanel _log;
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

    // Что сейчас показано справа (файл или коммит): при обновлении того же самого прокрутка сохраняется.
    private object? _diffKey;

    private const int LogHeight = 8;

    /// <param name="log">Журнал, в который <paramref name="git"/> пишет команды; показывается панелью по ~.</param>
    public MainWindow(GitService git, AuthStatus auth, ConfigStore config, CommandLog log)
    {
        _git = git;
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
            if (_branches.HasFocus)
                _ = LoadDiffAsync();
        };
        _commits.SelectionChanged += (_, _) =>
        {
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
        _diff.Mascot = config.Load().Mascot;

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
            _files, BoxTitle.ForFocusable(_files, "[1] Файлы"),
            _branches, BoxTitle.ForFocusable(_branches, "[2] Ветки"),
            _commits, BoxTitle.ForFocusable(_commits, "[3] Коммиты"),
            _stash, _stashTitle = BoxTitle.ForFocusable(_stash, "[4] Stash"),
            _diff, _diffTitle,
            _log, _logTitle,
            _status, _hints);
        // Справа — контекст панели в фокусе: diff файла, ветка или содержимое коммита.
        _files.HasFocusChanged += (_, _) => OnPanelFocusChanged(_files);
        _branches.HasFocusChanged += (_, _) => OnPanelFocusChanged(_branches);
        _commits.HasFocusChanged += (_, _) => OnPanelFocusChanged(_commits);
        _stash.HasFocusChanged += (_, _) => OnPanelFocusChanged(_stash);
        _stashTitle.Visible = false;
        RegisterCommands();
        _files.SetFocus();
        UpdateHints();
    }

    private void OnPanelFocusChanged(View panel)
    {
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
        }
        else
        {
            _watcher?.Dispose();
            _watcher = null;
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
            ShowStatus($"{Theme.SymbolWarning} Автообновление недоступно: {ex.Message}", Theme.Warning);
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
        _branches.HasFocus ? CommandScope.Branches
        : _commits.HasFocus ? CommandScope.Commits
        : _stash.HasFocus ? CommandScope.Stash
        : CommandScope.Files;

    /// <summary>
    /// Все команды экрана. Порядок — порядок в подсказках внизу и в справке.
    /// Клавиши панели проверяются раньше глобальных, поэтому space в файлах — stage, а в ветках — checkout.
    /// </summary>
    private void RegisterCommands()
    {
        void Add(string id, string title, CommandScope scope, CommandKey shortcut, Action execute, string? hint = null,
            bool hintOptional = false, Func<string?>? preview = null, bool dangerous = false, bool hidden = false,
            Func<bool>? available = null, string keywords = "") =>
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
            });

        Add("files.stage", "Stage / unstage", CommandScope.Files, CommandKey.Of("space", Key.Space),
            () => _ = ToggleSelectedAsync(), "stage", preview: StagePreview, keywords: "добавить в индекс стейдж");
        Add("files.stageAll", "Stage / unstage всех", CommandScope.Files, CommandKey.Of("a", Key.A),
            () => _ = ToggleAllAsync(), "все", hintOptional: true,
            preview: () => _files.Entries.Any(r => r.Section != FileSection.Staged) ? "git add --all" : "git reset --quiet", keywords: "добавить всё индекс");
        Add("files.ours", "Оставить нашу версию", CommandScope.Files, CommandKey.Of("o", Key.O),
            () => _ = ResolveConflictAsync(ours: true), "наша", available: IsConflictSelected,
            preview: () => ConflictPreview("--ours"), keywords: "конфликт ours наш мой");
        Add("files.theirs", "Оставить их версию", CommandScope.Files, CommandKey.Of("t", Key.T),
            () => _ = ResolveConflictAsync(ours: false), "их", available: IsConflictSelected,
            preview: () => ConflictPreview("--theirs"), keywords: "конфликт theirs их чужой");
        Add("files.both", "Оставить обе версии", CommandScope.Files, CommandKey.Of("b", Key.B),
            () => _ = ResolveBothAsync(), "оба", available: IsConflictSelected,
            preview: () => SelectedConflictFiles() is { Count: > 0 } f
                ? CommandText.Format(["merge-file", "--union", .. f.Select(x => x.Path)]) + " && git add …"
                : null,
            keywords: "конфликт оба обе union объединить вместе");
        Add("files.discard", "Сбросить изменения", CommandScope.Files, CommandKey.Of("d", Key.D),
            () => _ = DiscardSelectedAsync(), "сбросить", dangerous: true, keywords: "откатить отменить удалить ресет");

        Add("branches.checkout", "Checkout ветки", CommandScope.Branches, CommandKey.Of("space", Key.Space),
            () => _ = SwitchToSelectedAsync(), "checkout", preview: () => BranchPreview(b => ["switch", "--no-guess", b.Name]), keywords: "переключить чекаут ветка");
        Add("branches.new", "Новая ветка", CommandScope.Branches, CommandKey.Of("n", Key.N),
            OpenNewBranchDialog, "новая", preview: () => "git switch --create <имя>", keywords: "создать бранч");
        Add("branches.merge", "Merge в текущую", CommandScope.Branches, CommandKey.Of("M", Key.M.WithShift),
            () => _ = MergeSelectedAsync(), "merge", preview: () => BranchPreview(b => ["merge", "--no-edit", b.Name]), keywords: "мерж слить влить");
        Add("branches.delete", "Удалить ветку", CommandScope.Branches, CommandKey.Of("D", Key.D.WithShift),
            () => _ = DeleteSelectedBranchAsync(), "удалить", dangerous: true,
            preview: () => BranchPreview(b => GitService.DeleteBranchArguments(b, force: false)), keywords: "удалить бранч");

        Add("stash.pop", "Применить и удалить stash", CommandScope.Stash, CommandKey.Of("space", Key.Space),
            () => _ = StashActionAsync(pop: true), "pop", preview: () => StashPreview("pop"), keywords: "стэш достать pop");
        Add("stash.apply", "Применить stash", CommandScope.Stash, CommandKey.Of("a", Key.A),
            () => _ = StashActionAsync(pop: false), "apply", preview: () => StashPreview("apply"), keywords: "стэш apply");
        Add("stash.drop", "Удалить stash", CommandScope.Stash, CommandKey.Of("d", Key.D),
            () => _ = DropStashAsync(), "удалить", dangerous: true, preview: () => StashPreview("drop"), keywords: "стэш drop");

        Add("operation.abort", "Прервать merge / cherry-pick / rebase", CommandScope.Global, CommandKey.Of("A", Key.A.WithShift),
            () => _ = AbortOperationAsync(), "прервать", dangerous: true,
            available: () => _repoOperation != RepoOperation.None,
            preview: () => _repoOperation == RepoOperation.None ? null : CommandText.Format(GitService.AbortArguments(_repoOperation)),
            keywords: "abort отменить merge конфликт");
        Add("commit", "Коммит", CommandScope.Global, CommandKey.Of("c", Key.C),
            OpenCommitDialog, "коммит", preview: () => "git commit --file=- --cleanup=strip");
        Add("sync.pull", "Pull", CommandScope.Global, CommandKey.Of("p", Key.P),
            () => _ = PullAsync(), "pull", hintOptional: true, preview: () => "git pull --no-edit", keywords: "пул получить скачать");
        Add("sync.push", "Push / push -u", CommandScope.Global, CommandKey.Of("P", Key.P.WithShift),
            () => _ = PushAsync(), "push", hintOptional: true, preview: () => "git push", keywords: "пуш отправить опубликовать upstream");
        Add("stash.save", "Спрятать изменения в stash", CommandScope.Global, CommandKey.Of("s", Key.S),
            OpenStashDialog, "stash", hintOptional: true, preview: () => "git stash push --include-untracked",
            keywords: "стэш спрятать отложить сохранить");
        Add("sync.fetch", "Fetch", CommandScope.Global, CommandKey.Of("f", Key.F),
            () => _ = FetchAsync(), preview: () => "git fetch --all --prune", keywords: "фетч обновить сервер");

        Add("nav.next", "Следующая панель", CommandScope.Global, CommandKey.Of("tab", Key.Tab),
            () => FocusPanel(+1), "панель", hintOptional: true, hidden: true);
        Add("nav.previous", "Предыдущая панель", CommandScope.Global, CommandKey.Of("shift+tab", Key.Tab.WithShift),
            () => FocusPanel(-1), hidden: true);
        Add("nav.files", "Панель файлов", CommandScope.Global, CommandKey.Of("1", Key.D1), () => _files.SetFocus(), hidden: true);
        Add("nav.branches", "Панель веток", CommandScope.Global, CommandKey.Of("2", Key.D2), () => _branches.SetFocus(), hidden: true);
        Add("nav.commits", "Панель коммитов", CommandScope.Global, CommandKey.Of("3", Key.D3), () => _commits.SetFocus(), hidden: true);
        Add("nav.stash", "Панель stash", CommandScope.Global, CommandKey.Of("4", Key.D4), () => _stash.SetFocus(), hidden: true,
            available: () => _stash.Visible);
        Add("view.diffDown", "Листать diff вниз", CommandScope.Global, CommandKey.Of("ctrl+d", Key.D.WithCtrl),
            () => _diff.ScrollBy(Math.Max(1, _diff.Viewport.Height / 2)), hidden: true);
        Add("view.diffUp", "Листать diff вверх", CommandScope.Global, CommandKey.Of("ctrl+u", Key.U.WithCtrl),
            () => _diff.ScrollBy(-Math.Max(1, _diff.Viewport.Height / 2)), hidden: true);
        Add("history.undo", "Отменить последнее действие", CommandScope.Global, CommandKey.Of("z", Key.Z),
            () => _ = UndoAsync(), dangerous: true, keywords: "undo отмена откат reflog вернуть");
        Add("view.refresh", "Обновить", CommandScope.Global, CommandKey.Of("r", Key.R), () => _ = RefreshAsync(), keywords: "обновить");
        Add("view.commandLog", "Command Log", CommandScope.Global, CommandKey.Char('~'), ToggleCommandLog, keywords: "журнал лог история команд");

        Add("app.palette", "Палитра команд", CommandScope.Global,
            CommandKey.Any("ctrl+p / :", CommandKey.Of("ctrl+p", Key.P.WithCtrl), CommandKey.Char(':')), OpenPalette, hidden: true);
        Add("app.settings", "Настройки", CommandScope.Global, CommandKey.Char(','), OpenSettings, keywords: "настройки маскот параметры");
        Add("app.login", "Войти в GitHub", CommandScope.Global, CommandKey.Of("ctrl+g", Key.G.WithCtrl),
            Login, "войти", hintOptional: true, available: () => !_auth.IsAuthorized);
        Add("app.help", "Справка (также F1)", CommandScope.Global,
            CommandKey.Any("?", CommandKey.Char('?'), CommandKey.Of("F1", Key.F1)), OpenHelp, "справка");
        Add("app.back", "Назад к выбору репо", CommandScope.Global, CommandKey.Of("esc", Key.Esc), RequestStop, "назад");
        Add("app.quit", "Выход", CommandScope.Global, CommandKey.Of("q", Key.Q), () =>
        {
            Result = MainResult.Quit;
            RequestStop();
        }, "выход");
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
                    ? $"{Theme.SymbolWarning} Это уже отправлено в {_repoStatus?.Upstream}: после отмены история разойдётся, и для push понадобится --force-with-lease."
                    : "Ветка вернётся к предыдущему состоянию из reflog. Повторить действие можно заново, а найти прежний коммит — в git reflog.";
                if (!Confirm(plan.Question, plan.Args, warning, "Отменить"))
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
            ShowStatus($"{Theme.SymbolError} Нечего прятать в stash: изменений нет", Theme.Error);
            return;
        }

        var files = _files.Entries.Select(e => e.File.Path).Distinct().Count();
        using var dialog = new InputDialog("Stash", $"Спрятать {FilesWord(files)} (с новыми). Сообщение — по желанию",
            "спрятать");
        App!.Run(dialog);
        if (dialog.Result is not { } message)
            return;

        _ = RunAsync(ct => _git.StashAsync(message, ct), $"Изменения спрятаны в stash: {FilesWord(files)}");
    }

    private Task StashActionAsync(bool pop)
    {
        if (_stash.Selected is not { } stash)
            return Task.CompletedTask;

        return pop
            ? RunAsync(ct => _git.StashPopAsync(stash, ct), $"Stash применён и удалён: {stash.Message}")
            : RunAsync(ct => _git.StashApplyAsync(stash, ct), $"Stash применён и оставлен в списке: {stash.Message}");
    }

    private Task DropStashAsync()
    {
        if (_busy || _stash.Selected is not { } stash)
            return Task.CompletedTask;

        if (!Confirm($"Удалить stash «{stash.Message}»?", GitService.StashDropArguments(stash),
                "Спрятанные изменения пропадут. Это нельзя отменить.", "Удалить"))
            return Task.CompletedTask;

        return RunAsync(ct => _git.StashDropAsync(stash, ct), $"Stash удалён: {stash.Message}");
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
        var side = ours ? "наша версия" : "их версия";
        return RunAsync(ct => _git.ResolveConflictAsync(files, ours, ct),
            $"{name}: оставлена {side}. Вернуть конфликт: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<путь>")}");
    }

    /// <summary>Решает конфликт, оставляя обе версии подряд: сначала наши строки, затем их.</summary>
    private Task ResolveBothAsync()
    {
        var files = SelectedConflictFiles();
        if (files.Count == 0)
            return Task.CompletedTask;

        var name = files.Count == 1 ? files[0].Path : FilesWord(files.Count);
        return RunAsync(ct => _git.ResolveBothAsync(files, ct),
            $"{name}: оставлены обе версии — проверь порядок строк. Вернуть конфликт: git checkout -m -- {(files.Count == 1 ? files[0].Path : "<путь>")}");
    }

    /// <summary>Прерывает незавершённую операцию после подтверждения: возвращает состояние до её начала.</summary>
    private Task AbortOperationAsync()
    {
        if (_busy || _repoOperation == RepoOperation.None)
            return Task.CompletedTask;

        var operation = _repoOperation;
        var name = RepoHeader.OperationName(operation);
        if (!Confirm($"Прервать {name}?", GitService.AbortArguments(operation),
                $"Ветка вернётся к состоянию до {name}. Всё, что уже сделано при разрешении конфликтов, пропадёт.",
                "Прервать"))
            return Task.CompletedTask;

        return RunAsync(ct => _git.AbortAsync(operation, ct), $"{char.ToUpper(name[0])}{name[1..]} прерван, всё как до него");
    }

    private void FocusPanel(int step)
    {
        View[] panels = _stash.Visible ? [_files, _branches, _commits, _stash] : [_files, _branches, _commits];
        var current = Math.Max(0, Array.FindIndex(panels, p => p.HasFocus));
        panels[(current + step + panels.Length) % panels.Length].SetFocus();
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
            ShowStatus($"{Theme.SymbolError} gh не найден. Установи: sudo dnf install gh", Theme.Error);
            return;
        }

        Result = MainResult.Login;
        RequestStop();
    }

    private void OpenHelp()
    {
        using var help = new HelpDialog(Viewport.Height, _commands.All.Where(c => c.IsAvailable()).ToList());
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
            ShowStatus($"{Theme.SymbolSuccess} Ты уже на ветке {branch.Name}", Theme.Muted, autoHide: true);
            return Task.CompletedTask;
        }

        return RunAsync(ct => _git.SwitchAsync(branch, ct), $"Переключено на {branch.Name}");
    }

    /// <summary>Создаёт ветку от текущего HEAD и переключается на неё.</summary>
    private void OpenNewBranchDialog()
    {
        if (_busy)
            return;

        var head = _repoStatus?.HeadHash is { Length: >= 7 } hash ? $" {Theme.SymbolSeparator} {hash[..7]}" : "";
        var from = (_repoStatus?.Branch ?? "HEAD") + head;
        var existing = _branches.Items.Select(b => b.Name).ToList();
        using var dialog = new InputDialog("Новая ветка", $"От: {from}", "создать и переключиться",
            name => BranchName.Check(name, existing));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return;

        _ = RunAsync(ct => _git.CreateBranchAsync(name, ct), $"Создана ветка {name}, ты на ней");
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
            ShowStatus($"{Theme.SymbolError} Нельзя удалить текущую ветку. Переключись на другую (space) и повтори", Theme.Error);
            return;
        }

        if (!Confirm($"Удалить ветку {branch.Name}?", GitService.DeleteBranchArguments(branch, force: false),
                "Если в ветке есть коммиты, которых нет в текущей, git откажет и предложит удалить принудительно.",
                "Удалить"))
            return;

        var result = await RunAsync(ct => _git.DeleteBranchAsync(branch, force: false, ct), $"Ветка {branch.Name} удалена");
        if (result is null || result.Success || !result.StdErr.Contains("is not fully merged"))
            return;

        var unmerged = await _git.CountUnmergedAsync(branch);
        App?.Invoke(() =>
        {
            if (Confirm($"Ветка {branch.Name} не влита. Удалить всё равно?", GitService.DeleteBranchArguments(branch, force: true),
                    $"Коммитов, которых нет в текущей ветке: {unmerged}. Они пропадут, вернуть их можно только через git reflog.",
                    "Удалить всё равно"))
                _ = RunAsync(ct => _git.DeleteBranchAsync(branch, force: true, ct), $"Ветка {branch.Name} удалена");
        });
    }

    /// <summary>Вливает выбранную ветку в текущую.</summary>
    private Task MergeSelectedAsync()
    {
        if (_branches.Selected is not { } branch)
            return Task.CompletedTask;

        if (branch.IsCurrent)
        {
            ShowStatus($"{Theme.SymbolError} Это текущая ветка. Выбери ветку, которую нужно влить в {branch.Name}", Theme.Error);
            return Task.CompletedTask;
        }

        var target = _repoStatus?.Branch ?? "HEAD";
        return RunAsync(ct => _git.MergeAsync(branch, ct), result => Task.FromResult(
            result.StdOut.Contains("Already up to date") ? $"{target} уже содержит все коммиты {branch.Name}"
            : result.StdOut.Contains("Fast-forward") ? $"{branch.Name} влита в {target} (fast-forward)"
            : $"{branch.Name} влита в {target}, создан merge-коммит"));
    }

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
            FileSection.Staged => RunAsync(ct => _git.UnstageAsync(files, ct), $"{name} убран из индекса"),
            FileSection.Conflicts => RunAsync(ct => _git.StageAsync(files, ct), $"{name}: конфликт отмечен как решённый"),
            _ => RunAsync(ct => _git.StageAsync(files, ct), $"{name} добавлен в индекс"),
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
        var where = isFolder ? $"в папке {name}" : $"в {name}";

        if (section == FileSection.Conflicts)
        {
            ShowStatus($"{Theme.SymbolError} Конфликт так не сбросить: реши его в файле и добавь в индекс (space)", Theme.Error);
            return Task.CompletedTask;
        }

        if (section == FileSection.Staged && _repoStatus?.HeadHash is null)
        {
            ShowStatus($"{Theme.SymbolError} Коммитов ещё нет: убери файл из индекса (space), затем удали его как новый (d)", Theme.Error);
            return Task.CompletedTask;
        }

        var (args, question, warning, confirmText, action) = section switch
        {
            FileSection.Untracked => (
                GitService.DeleteUntrackedArguments(files),
                isFolder ? $"Удалить новые файлы в папке {name}?" : $"Удалить {name}?",
                "Новые файлы ещё не в git: они будут удалены с диска, вернуть их будет нельзя.",
                "Удалить",
                (Func<CancellationToken, Task<GitResult>>)(ct => _git.DeleteUntrackedAsync(files, ct))),
            FileSection.Staged => (
                GitService.DiscardToHeadArguments(files),
                $"Сбросить все изменения {where}?",
                "Файлы вернутся к последнему коммиту: и staged, и несохранённые в индексе изменения пропадут. Это нельзя отменить.",
                "Сбросить",
                ct => _git.DiscardToHeadAsync(files, ct)),
            _ => (
                GitService.DiscardWorktreeArguments(files),
                $"Сбросить изменения {where}?",
                "Изменения, не добавленные в индекс, пропадут. Staged-часть останется. Это нельзя отменить.",
                "Сбросить",
                ct => _git.DiscardWorktreeAsync(files, ct)),
        };

        if (!Confirm(question, args, warning, confirmText))
            return Task.CompletedTask;

        var done = section == FileSection.Untracked ? $"Удалено: {name}" : $"Изменения сброшены: {name}";
        return RunAsync(action, done);
    }

    /// <summary>Если есть изменения вне индекса — добавляет всё, иначе убирает всё из индекса.</summary>
    private Task ToggleAllAsync()
    {
        var hasUnstaged = _files.Entries.Any(r => r.Section != FileSection.Staged);
        if (hasUnstaged)
            return RunAsync(ct => _git.StageAllAsync(ct), "Все изменения добавлены в индекс");

        return _files.Entries.Count > 0
            ? RunAsync(ct => _git.UnstageAllAsync(ct), "Все изменения убраны из индекса")
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
            ShowStatus($"{Theme.SymbolError} Нечего коммитить: добавь файлы в индекс клавишей space или a", Theme.Error);
            return;
        }

        using var dialog = new CommitDialog(stagedCount);
        App!.Run(dialog);
        if (dialog.Result is not { } message)
            return;

        _ = RunAsync(ct => _git.CommitAsync(message, ct), async _ =>
        {
            var hash = await _git.GetShortHeadAsync();
            return $"Коммит {hash} создан: {message}";
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
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} Отменено: {progress?.TrimEnd('…')}", Theme.Error));
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
                ? $"На сервере {TextDrawing.Plural(status.Behind, "новый коммит", "новых коммита", "новых коммитов")}. Нажми p, чтобы забрать"
                : "Fetch выполнен, новых коммитов нет";
        }, "Получение изменений с сервера…");

    private Task PullAsync()
    {
        var upstream = _repoStatus?.Upstream ?? "upstream";
        return RunAsync(ct => _git.PullAsync(ct), result => Task.FromResult(
            result.StdOut.Contains("Already up to date") ? $"Уже актуально: в {upstream} нет новых коммитов"
            : result.StdOut.Contains("Fast-forward") ? $"Pull из {upstream} выполнен (fast-forward)"
            : $"Pull из {upstream} выполнен, изменения слиты"), $"Pull из {upstream}…");
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
            ShowStatus($"{Theme.SymbolError} HEAD не на ветке (detached). Создай ветку (n в панели веток) и сделай push", Theme.Error);
            return;
        }

        if (status.Upstream is { } upstream)
        {
            _pushUpstreamOffer = null;
            var count = status.Ahead > 0 ? TextDrawing.Plural(status.Ahead, "коммит", "коммита", "коммитов") + " " : "";
            await RunAsync(ct => _git.PushAsync(ct), $"Отправлено {count}в {upstream}", $"Отправка в {upstream}…");
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
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} Remote не настроен. Добавь его: git remote add origin <адрес>", Theme.Error));
            return;
        }

        var remote = remotes[0];
        var command = CommandText.Format(GitService.PushSetUpstreamArguments(remote, branch));
        if (_pushUpstreamOffer != branch)
        {
            _pushUpstreamOffer = branch;
            App?.Invoke(() => ShowStatus(
                $"{Theme.SymbolWarning} Нет upstream для {branch}. Нажми P ещё раз, чтобы выполнить {command}", Theme.Warning));
            return;
        }

        _pushUpstreamOffer = null;
        await RunAsync(ct => _git.PushSetUpstreamAsync(remote, branch, ct),
            $"Ветка {branch} опубликована в {remote}, upstream настроен", $"Публикация {branch} в {remote}…");
    }

    /// <summary>Спиннер в строке статуса: «⠹ Отправка в origin/main…  esc отмена».</summary>
    private void StartSpinner(string text)
    {
        ShowStatus("", Theme.Muted);
        var frame = 0;
        void Draw() => _status.Text = $"{Theme.SpinnerFrames[frame % Theme.SpinnerFrames.Length]} {text}  esc отмена";
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
            App?.Invoke(() => ShowStatus($"{Theme.SpinnerFrames[0]} Обновление…", Theme.Muted));
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
            var logTask = _git.GetLogAsync();
            var stashTask = _git.GetStashesAsync();
            var operationTask = _git.GetOperationAsync();
            var status = await statusTask;
            var branches = await branchesTask;
            var log = await logTask;
            var stashes = await stashTask;
            var operation = await operationTask;
            App?.Invoke(() =>
            {
                _repoStatus = status;
                _repoOperation = operation;
                _header.SetStatus(status, operation);
                _files.SetStatus(status);
                _branches.SetItems(branches);
                _commits.SetItems(log);
                SetStashes(stashes);
                UpdateHints();
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
        if (_branches.HasFocus)
            return ShowBranchInfo();
        if (_stash.HasFocus)
            return LoadStashDiffAsync();
        return _commits.HasFocus ? LoadCommitDiffAsync() : LoadFileDiffAsync();
    }

    private Task ShowBranchInfo()
    {
        _diffKey = null;
        if (_branches.Selected is not { } branch)
        {
            _diffTitle.Text = " Ветка ";
            _diff.SetMessage("Веток пока нет: сделай первый коммит");
            return Task.CompletedTask;
        }

        _diffTitle.Text = $" Ветка {Theme.SymbolSeparator} {branch.Name} ";
        var upstream = branch switch
        {
            { IsUpstreamGone: true } => $"upstream {branch.Upstream} удалён на сервере",
            { Upstream: { } name } => $"upstream {name} {Theme.SymbolAhead}{branch.Ahead} {Theme.SymbolBehind}{branch.Behind}",
            _ => "без upstream",
        };
        var action = branch.IsCurrent ? "текущая ветка" : "space — переключиться";
        _diff.SetMessage($"{Theme.SymbolBranch} {branch.Name} {Theme.SymbolSeparator} {upstream} {Theme.SymbolSeparator} {action}");
        return Task.CompletedTask;
    }

    private Task LoadCommitDiffAsync()
    {
        if (_commits.Selected is not { } commit)
        {
            _diffKey = null;
            _diffTitle.Text = " Коммит ";
            _diff.SetMessage("Коммитов пока нет");
            return Task.CompletedTask;
        }

        return ShowDiffAsync(commit.Hash, $" Коммит · {commit.ShortHash} ", async ct =>
        {
            var diff = await _git.GetCommitDiffAsync(commit, ct);
            return await Task.Run(() => DiffPanel.PrepareCommit(commit, diff, DateTimeOffset.Now), ct);
        });
    }

    private Task LoadStashDiffAsync()
    {
        if (_stash.Selected is not { } stash)
            return Task.CompletedTask;

        return ShowDiffAsync(stash.Hash, $" Stash · {stash.Ref} ", async ct =>
        {
            var diff = await _git.GetStashDiffAsync(stash, ct);
            var asCommit = new Commit(stash.Hash, stash.AuthorName, stash.AuthorEmail, stash.Date, stash.Message);
            return await Task.Run(() => DiffPanel.PrepareCommit(asCommit, diff, DateTimeOffset.Now), ct);
        });
    }

    private Task LoadFileDiffAsync()
    {
        if (_files.SelectedDirectory is { } directory)
        {
            _diffKey = null;
            _diffTitle.Text = $" Diff · {directory.Path}/ ";
            var files = directory.Files.Count;
            var action = directory.Collapsed ? "развернуть" : "свернуть";
            _diff.SetMessage($"Папка: {FilesWord(files)}. space — stage всей папки, enter — {action}");
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
        var tag = staged ? " · staged" : row.File.IsUntracked ? " · новый файл" : "";
        return ShowDiffAsync((row.Section, row.File.Path), $" Diff · {row.File.Path}{tag} ", async ct =>
        {
            var lines = await _git.GetDiffAsync(row.File, staged, ct);
            return await Task.Run(() => DiffPanel.Prepare(lines, row.File.Path), ct);
        });
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

    private static string FilesWord(int count) => TextDrawing.Plural(count, "файл", "файла", "файлов");

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
