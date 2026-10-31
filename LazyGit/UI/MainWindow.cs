using LazyGit.Auth;
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
/// Главный экран репозитория: шапка, слева панели файлов и коммитов, справа diff.
/// 1 / 3 и tab — переход между панелями. В файлах: space — stage/unstage файла, a — всех файлов.
/// c — коммит, r — обновить, ? — справка.
/// Экран обновляется автоматически при изменении файлов и состояния git.
/// Result: Quit (q), Login (ctrl+g — вход в GitHub) или null (Esc — назад к стартовому экрану).
/// </summary>
public sealed class MainWindow : Runnable<MainResult?>
{
    private readonly GitService _git;
    private readonly AuthStatus _auth;
    private readonly RepoHeader _header;
    private readonly FilesPanel _files;
    private readonly CommitsPanel _commits;
    private readonly DiffPanel _diff;
    private readonly Label _diffTitle;
    private readonly Label _status;
    private readonly HintBar _hints;
    private bool _busy;
    private object? _statusTimeout;
    private CancellationTokenSource? _diffLoading;
    private RepoWatcher? _watcher;
    private bool _refreshPending;

    // Что сейчас показано справа (файл или коммит): при обновлении того же самого прокрутка сохраняется.
    private object? _diffKey;

    public MainWindow(GitService git, AuthStatus auth)
    {
        _git = git;
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
            Height = Dim.Percent(55),
        };

        _commits = new CommitsPanel
        {
            X = 0,
            Y = Pos.Bottom(_files),
            Width = Dim.Width(_files),
            Height = Dim.Fill(2),
        };
        _files.SelectionChanged += (_, _) =>
        {
            if (!_commits.HasFocus)
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

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        _hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };

        Add(
            _header, BoxTitle.For(_header, "LGit", Theme.Accent),
            _files, BoxTitle.ForFocusable(_files, "[1] Файлы"),
            _commits, BoxTitle.ForFocusable(_commits, "[3] Коммиты"),
            _diff, _diffTitle,
            _status, _hints);
        // Справа — контекст панели в фокусе: diff файла или содержимое коммита.
        _files.HasFocusChanged += (_, _) => OnPanelFocusChanged(_files);
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
            hints.Add(new Hint("↑↓", "выбор коммита"));
        }
        else
        {
            hints.Add(new Hint("space", "stage"));
            hints.Add(new Hint("a", "все"));
        }

        hints.Add(new Hint("c", "коммит"));
        hints.Add(new Hint("ctrl+d/u", "diff"));
        hints.Add(new Hint("tab", "панель"));
        hints.Add(new Hint("r", "обновить"));
        if (!_auth.IsAuthorized)
            hints.Add(new Hint("ctrl+g", "войти"));
        hints.Add(new Hint("?", "справка"));
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

        if (key == Key.D3)
        {
            _commits.SetFocus();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            // Панелей пока две, поэтому tab и shift+tab просто переключают между ними.
            if (_files.HasFocus)
                _commits.SetFocus();
            else
                _files.SetFocus();
            return true;
        }

        if (key == Key.Space && _files.HasFocus)
        {
            _ = ToggleSelectedAsync();
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

        if (key.AsRune.Value == '?' || key == Key.F1)
        {
            using var help = new HelpDialog();
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

        _ = RunAsync(ct => _git.CommitAsync(message, ct), async () =>
        {
            var hash = await _git.GetShortHeadAsync();
            return $"Коммит {hash} создан: {message}";
        });
    }

    private Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string successMessage) =>
        RunAsync(action, () => Task.FromResult(successMessage));

    /// <summary>Выполняет изменяющую команду, показывает результат и обновляет экран. Одновременно — одна команда.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, Func<Task<string>> successMessage)
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            var result = await action(CancellationToken.None);
            var success = result.Success ? await successMessage() : null;
            await LoadStatusAsync();
            App?.Invoke(() =>
            {
                if (success is not null)
                    ShowStatus($"{Theme.SymbolSuccess} {success}", Theme.Success, autoHide: true);
                else
                    ShowStatus($"{Theme.SymbolError} {ExplainError(result.StdErr)}", Theme.Error);
            });
        }
        finally
        {
            _busy = false;
            RunPendingRefresh();
        }
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
            var logTask = _git.GetLogAsync();
            var status = await statusTask;
            var log = await logTask;
            App?.Invoke(() =>
            {
                _header.SetStatus(status);
                _files.SetStatus(status);
                _commits.SetCommits(log);
            });
            return true;
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {FirstLine(ex.Result.StdErr)}", Theme.Error));
            return false;
        }
    }

    /// <summary>Понятный текст для известных ошибок git, иначе — первая строка stderr.</summary>
    private static string ExplainError(string stderr)
    {
        if (stderr.Contains("Author identity unknown") || stderr.Contains("Please tell me who you are"))
            return "Git не знает имя автора. Выполни: git config --global user.name \"Имя\" и user.email \"почта\"";
        return FirstLine(stderr);
    }

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline];
    }

    /// <summary>
    /// Показывает справа выбранный коммит, если в фокусе панель коммитов, иначе — diff выбранного файла.
    /// Предыдущая незавершённая загрузка отменяется.
    /// </summary>
    private Task LoadDiffAsync()
    {
        _diffLoading?.Cancel();
        return _commits.HasFocus ? LoadCommitDiffAsync() : LoadFileDiffAsync();
    }

    private Task LoadCommitDiffAsync()
    {
        if (_commits.SelectedCommit is not { } commit)
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
            App?.Invoke(() => _diff.SetMessage($"{Theme.SymbolError} {FirstLine(ex.Result.StdErr)}"));
        }
    }

    private static string FilesWord(int count) => TextDrawing.Plural(count, "файл", "файла", "файлов");

    /// <summary>Показывает сообщение над подсказками. С autoHide сообщение исчезает через 3 секунды.</summary>
    private void ShowStatus(string text, Color color, bool autoHide = false)
    {
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
