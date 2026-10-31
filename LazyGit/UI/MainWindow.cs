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
    private bool _refreshing;

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

        return base.OnKeyDown(key);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing)
            return;

        _refreshing = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} Обновление…", Theme.Muted);
        try
        {
            var status = await _git.GetStatusAsync();
            App?.Invoke(() =>
            {
                _header.SetStatus(status);
                _files.SetStatus(status);
                ShowStatus("", Theme.Muted);
            });
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {ex.Result.StdErr.Trim()}", Theme.Error));
        }
        finally
        {
            _refreshing = false;
        }
    }

    // TODO(этап 2): настоящий diff выбранного файла.
    private void UpdateDiffPlaceholder()
    {
        _diffPlaceholder.Text = _files.SelectedFile is { } row
            ? $"{row.File.Path}\n\nDiff появится на следующем шаге."
            : "";
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
