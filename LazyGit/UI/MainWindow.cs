using LazyGit.Auth;
using LazyGit.Git;
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
/// Главный экран репозитория: шапка, панель файлов слева, diff справа.
/// space — stage/unstage выбранного файла, a — всех файлов, c — коммит, r — обновить, ? — справка.
/// Экран обновляется автоматически при изменении файлов и состояния git.
/// Result: Quit (q), Login (ctrl+g — вход в GitHub) или null (Esc — назад к стартовому экрану).
/// </summary>
public sealed class MainWindow : Runnable<MainResult?>
{
    private readonly GitService _git;
    private readonly AuthStatus _auth;
    private readonly RepoHeader _header;
    private readonly FilesPanel _files;
    private readonly DiffPanel _diff;
    private readonly Label _diffTitle;
    private readonly Label _status;
    private bool _busy;
    private object? _statusTimeout;
    private CancellationTokenSource? _diffLoading;
    private RepoWatcher? _watcher;
    private bool _refreshPending;

    // Файл, diff которого сейчас показан: при обновлении того же файла прокрутка сохраняется.
    private (FileSection Section, string Path)? _diffKey;

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
            Height = Dim.Fill(2),
        };
        _files.SelectionChanged += (_, _) => _ = LoadDiffAsync();

        _diff = new DiffPanel
        {
            X = Pos.Right(_files),
            Y = Pos.Top(_files),
            Width = Dim.Fill(),
            Height = Dim.Height(_files),
        };
        _diffTitle = BoxTitle.For(_diff, "Diff", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        var hintList = new List<Hint>
        {
            new("space", "stage"),
            new("a", "все"),
            new("c", "коммит"),
            new("ctrl+d/u", "diff"),
            new("r", "обновить"),
        };
        if (!auth.IsAuthorized)
            hintList.Add(new Hint("ctrl+g", "войти"));
        hintList.Add(new Hint("?", "справка"));
        hintList.Add(new Hint("q", "выход"));
        hints.SetHints(hintList);

        Add(
            _header, BoxTitle.For(_header, "LGit", Theme.Accent),
            _files, BoxTitle.For(_files, "[1] Файлы", Theme.Accent),
            _diff, _diffTitle,
            _status, hints);
        _files.SetFocus();
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

        if (key == Key.Space)
        {
            _ = ToggleSelectedAsync();
            return true;
        }

        if (key == Key.A)
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

    /// <summary>Stage для файла из «Изменений», Untracked и конфликтов, unstage — для файла из Staged.</summary>
    private Task ToggleSelectedAsync()
    {
        if (_files.SelectedFile is not { } row)
            return Task.CompletedTask;

        var file = row.File;
        return row.Section switch
        {
            FileSection.Staged => RunAsync(ct => _git.UnstageAsync(file, ct), $"{file.Path} убран из индекса"),
            FileSection.Conflicts => RunAsync(ct => _git.StageAsync(file, ct), $"{file.Path}: конфликт отмечен как решённый"),
            _ => RunAsync(ct => _git.StageAsync(file, ct), $"{file.Path} добавлен в индекс"),
        };
    }

    /// <summary>Если есть изменения вне индекса — добавляет всё, иначе убирает всё из индекса.</summary>
    private Task ToggleAllAsync()
    {
        var hasUnstaged = _files.Rows.OfType<FileEntryRow>().Any(r => r.Section != FileSection.Staged);
        if (hasUnstaged)
            return RunAsync(ct => _git.StageAllAsync(ct), "Все изменения добавлены в индекс");

        return _files.Rows.Count > 0
            ? RunAsync(ct => _git.UnstageAllAsync(ct), "Все изменения убраны из индекса")
            : Task.CompletedTask;
    }

    /// <summary>Открывает окно коммита, если в индексе есть изменения.</summary>
    private void OpenCommitDialog()
    {
        if (_busy)
            return;

        var stagedCount = _files.Rows.OfType<FileEntryRow>().Count(r => r.Section == FileSection.Staged);
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

    /// <summary>Перечитывает статус и обновляет шапку и панель файлов. Возвращает false при ошибке.</summary>
    private async Task<bool> LoadStatusAsync()
    {
        try
        {
            var status = await _git.GetStatusAsync();
            App?.Invoke(() =>
            {
                _header.SetStatus(status);
                _files.SetStatus(status);
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

    /// <summary>Загружает diff выбранного файла. Предыдущая незавершённая загрузка отменяется.</summary>
    private async Task LoadDiffAsync()
    {
        _diffLoading?.Cancel();
        if (_files.SelectedFile is not { } row)
        {
            _diffKey = null;
            _diffTitle.Text = " Diff ";
            _diff.SetMessage("");
            return;
        }

        var key = (row.Section, row.File.Path);
        var sameFile = _diffKey == key;
        _diffKey = key;

        var loading = new CancellationTokenSource();
        _diffLoading = loading;
        var staged = row.Section == FileSection.Staged;
        var tag = staged ? " · staged" : row.File.IsUntracked ? " · новый файл" : "";
        _diffTitle.Text = $" Diff · {row.File.Path}{tag} ";

        try
        {
            var lines = await _git.GetDiffAsync(row.File, staged, loading.Token);
            var prepared = await Task.Run(() => DiffPanel.Prepare(lines, row.File.Path), loading.Token);
            App?.Invoke(() =>
            {
                if (!loading.IsCancellationRequested)
                    _diff.SetDiff(prepared, keepScroll: sameFile);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбран другой файл.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _diff.SetMessage($"{Theme.SymbolError} {FirstLine(ex.Result.StdErr)}"));
        }
    }

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
