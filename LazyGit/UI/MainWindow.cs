using LazyGit.Auth;
using LazyGit.Git;
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
}

/// <summary>
/// Главный экран репозитория: шапка, панель файлов слева, diff справа.
/// space — stage/unstage выбранного файла, a — всех файлов, r — обновить.
/// Result: Quit (q) или null (Esc — назад к стартовому экрану).
/// </summary>
public sealed class MainWindow : Runnable<MainResult?>
{
    private readonly GitService _git;
    private readonly RepoHeader _header;
    private readonly FilesPanel _files;
    private readonly View _diff;
    private readonly Label _diffPlaceholder;
    private readonly Label _status;
    private bool _busy;
    private object? _statusTimeout;

    public MainWindow(GitService git, AuthStatus auth)
    {
        _git = git;
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
        _files.SelectionChanged += (_, _) => UpdateDiffPlaceholder();

        _diff = new View
        {
            X = Pos.Right(_files),
            Y = Pos.Top(_files),
            Width = Dim.Fill(),
            Height = Dim.Height(_files),
            BorderStyle = LineStyle.Rounded,
        };
        _diff.SetScheme(Theme.Text(Theme.Dim));
        _diffPlaceholder = new Label { X = 1, Y = 0, Width = Dim.Fill(1) };
        _diffPlaceholder.SetScheme(Theme.Text(Theme.Muted));
        _diff.Add(_diffPlaceholder);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };

        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("↑↓", "навигация"),
            new Hint("space", "stage/unstage"),
            new Hint("a", "все"),
            new Hint("r", "обновить"),
            new Hint("esc", "назад"),
            new Hint("q", "выход"),
        ]);

        Add(
            _header, BoxTitle.For(_header, "LGit", Theme.Accent),
            _files, BoxTitle.For(_files, "[1] Файлы", Theme.Accent),
            _diff, BoxTitle.For(_diff, "Diff", Theme.Muted),
            _status, hints);
        _files.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _ = RefreshAsync();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q)
        {
            Result = MainResult.Quit;
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

    /// <summary>Выполняет изменяющую команду, показывает результат и обновляет экран. Одновременно — одна команда.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string successMessage)
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            var result = await action(CancellationToken.None);
            await LoadStatusAsync();
            App?.Invoke(() =>
            {
                if (result.Success)
                    ShowStatus($"{Theme.SymbolSuccess} {successMessage}", Theme.Success, autoHide: true);
                else
                    ShowStatus($"{Theme.SymbolError} {FirstLine(result.StdErr)}", Theme.Error);
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task RefreshAsync()
    {
        if (_busy)
            return;

        _busy = true;
        App?.Invoke(() => ShowStatus($"{Theme.SpinnerFrames[0]} Обновление…", Theme.Muted));
        try
        {
            if (await LoadStatusAsync())
                App?.Invoke(() => ShowStatus("", Theme.Muted));
        }
        finally
        {
            _busy = false;
        }
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

    private static string FirstLine(string text)
    {
        var trimmed = text.Trim();
        var newline = trimmed.IndexOf('\n');
        return newline < 0 ? trimmed : trimmed[..newline];
    }

    // TODO(этап 2): настоящий diff выбранного файла.
    private void UpdateDiffPlaceholder()
    {
        _diffPlaceholder.Text = _files.SelectedFile is { } row
            ? $"{row.File.Path}\n\nDiff появится на следующем шаге."
            : "";
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
