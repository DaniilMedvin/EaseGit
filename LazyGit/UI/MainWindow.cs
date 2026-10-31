using LazyGit.Auth;
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
/// «,» — настройки, c — коммит, f — fetch, p — pull, P — push (во время операции esc отменяет её), r — обновить, ? — справка.
/// Экран обновляется автоматически при изменении файлов и состояния git.
/// Result: Quit (q), Login (ctrl+g — вход в GitHub) или null (Esc — назад к стартовому экрану).
/// </summary>
public sealed class MainWindow : Runnable<MainResult?>
{
    private readonly GitService _git;
    private readonly ConfigStore _config;
    private readonly AuthStatus _auth;
    private readonly RepoHeader _header;
    private readonly FilesPanel _files;
    private readonly BranchesPanel _branches;
    private readonly CommitsPanel _commits;
    private readonly DiffPanel _diff;
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

    // Что сейчас показано справа (файл или коммит): при обновлении того же самого прокрутка сохраняется.
    private object? _diffKey;

    public MainWindow(GitService git, AuthStatus auth, ConfigStore config)
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
        // Справа — контекст панели в фокусе, поэтому выбор в остальных панелях его не меняет.
        _files.SelectionChanged += (_, _) =>
        {
            if (!_branches.HasFocus && !_commits.HasFocus)
                _ = LoadDiffAsync();
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

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        _hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };

        Add(
            _header, BoxTitle.For(_header, "LGit", Theme.Accent),
            _files, BoxTitle.ForFocusable(_files, "[1] Файлы"),
            _branches, BoxTitle.ForFocusable(_branches, "[2] Ветки"),
            _commits, BoxTitle.ForFocusable(_commits, "[3] Коммиты"),
            _diff, _diffTitle,
            _status, _hints);
        // Справа — контекст панели в фокусе: diff файла, ветка или содержимое коммита.
        _files.HasFocusChanged += (_, _) => OnPanelFocusChanged(_files);
        _branches.HasFocusChanged += (_, _) => OnPanelFocusChanged(_branches);
        _commits.HasFocusChanged += (_, _) => OnPanelFocusChanged(_commits);
        _files.SetFocus();
        UpdateHints();
    }

    private void OnPanelFocusChanged(View panel)
    {
        UpdateHints();
        if (panel.HasFocus)
            _ = LoadDiffAsync();
    }

    /// <summary>Подсказки внизу зависят от панели в фокусе.</summary>
    private void UpdateHints()
    {
        var hints = new List<Hint>();
        if (_commits.HasFocus)
        {
            hints.Add(new Hint("↑↓", "выбор коммита", Optional: true));
        }
        else if (_branches.HasFocus)
        {
            hints.Add(new Hint("space", "checkout"));
            hints.Add(new Hint("n", "новая"));
            hints.Add(new Hint("M", "merge"));
            hints.Add(new Hint("D", "удалить"));
        }
        else
        {
            hints.Add(new Hint("space", "stage"));
            hints.Add(new Hint("a", "все", Optional: true));
            hints.Add(new Hint("d", "сбросить"));
        }

        // Остальные клавиши (ctrl+d/u, r, f) — в справке: на 80 колонок всё не помещается.
        hints.Add(new Hint("c", "коммит"));
        hints.Add(new Hint("p/P", "pull/push", Optional: true));
        hints.Add(new Hint("tab", "панель", Optional: true));
        if (!_auth.IsAuthorized)
            hints.Add(new Hint("ctrl+g", "войти", Optional: true));
        hints.Add(new Hint("?", "справка"));
        hints.Add(new Hint("esc", "назад"));
        hints.Add(new Hint("q", "выход"));
        _hints.SetHints(hints);
    }

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

        if (key == Key.F)
        {
            _ = FetchAsync();
            return true;
        }

        if (key == Key.P)
        {
            _ = PullAsync();
            return true;
        }

        if (key == Key.P.WithShift)
        {
            _ = PushAsync();
            return true;
        }

        if (key == Key.Q)
        {
            Result = MainResult.Quit;
            RequestStop();
            return true;
        }

        if (key == Key.G.WithCtrl && !_auth.IsAuthorized)
        {
            if (!_auth.GhInstalled)
            {
                ShowStatus($"{Theme.SymbolError} gh не найден. Установи: sudo dnf install gh", Theme.Error);
                return true;
            }

            Result = MainResult.Login;
            RequestStop();
            return true;
        }

        if (key == Key.R)
        {
            _ = RefreshAsync();
            return true;
        }

        if (key == Key.D1)
        {
            _files.SetFocus();
            return true;
        }

        if (key == Key.D2)
        {
            _branches.SetFocus();
            return true;
        }

        if (key == Key.D3)
        {
            _commits.SetFocus();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            View[] panels = [_files, _branches, _commits];
            var current = Array.FindIndex(panels, p => p.HasFocus);
            var step = key == Key.Tab ? 1 : panels.Length - 1;
            panels[(Math.Max(current, 0) + step) % panels.Length].SetFocus();
            return true;
        }

        if (key == Key.Space && _branches.HasFocus)
        {
            _ = SwitchToSelectedAsync();
            return true;
        }

        if (key == Key.N && _branches.HasFocus)
        {
            OpenNewBranchDialog();
            return true;
        }

        if (key == Key.D.WithShift && _branches.HasFocus)
        {
            _ = DeleteSelectedBranchAsync();
            return true;
        }

        if (key == Key.M.WithShift && _branches.HasFocus)
        {
            _ = MergeSelectedAsync();
            return true;
        }

        if (key == Key.Space && _files.HasFocus)
        {
            _ = ToggleSelectedAsync();
            return true;
        }

        if (key == Key.D && _files.HasFocus)
        {
            _ = DiscardSelectedAsync();
            return true;
        }

        if (key == Key.A && _files.HasFocus)
        {
            _ = ToggleAllAsync();
            return true;
        }

        if (key == Key.C)
        {
            OpenCommitDialog();
            return true;
        }

        if (key.AsRune.Value == ',')
        {
            OpenSettings();
            return true;
        }

        if (key.AsRune.Value == '?' || key == Key.F1)
        {
            using var help = new HelpDialog(Viewport.Height);
            App!.Run(help);
            return true;
        }

        if (key == Key.D.WithCtrl)
        {
            _diff.ScrollBy(Math.Max(1, _diff.Viewport.Height / 2));
            return true;
        }

        if (key == Key.U.WithCtrl)
        {
            _diff.ScrollBy(-Math.Max(1, _diff.Viewport.Height / 2));
            return true;
        }

        return base.OnKeyDown(key);
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
        using var dialog = new NewBranchDialog(from, _branches.Items.Select(b => b.Name).ToList());
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
            var status = await statusTask;
            var branches = await branchesTask;
            var log = await logTask;
            App?.Invoke(() =>
            {
                _repoStatus = status;
                _header.SetStatus(status);
                _files.SetStatus(status);
                _branches.SetItems(branches);
                _commits.SetItems(log);
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
