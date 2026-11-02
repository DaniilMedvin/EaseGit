using EaseGit.Git;
using EaseGit.Models;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI;

/// <summary>
/// История файла: сверху коммиты, менявшие его (через переименования), снизу выбранный коммит с diff только этого файла.
/// a — показать коммит целиком / только файл, y — скопировать хеш, ctrl+d/u — листать, esc — закрыть.
/// </summary>
public sealed class FileHistoryWindow : Runnable
{
    private readonly GitService _git;
    private readonly string _path;
    private readonly FileRevisionsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private CancellationTokenSource? _detailsLoading;
    private bool _wholeCommit;

    public FileHistoryWindow(GitService git, string path)
    {
        _git = git;
        _path = path;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _list = new FileRevisionsPanel { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(40) };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, T("Коммит", "Commit"), Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("↑↓", T("коммит", "commit")),
            new Hint("a", T("весь коммит / файл", "whole commit / file")),
            new Hint("b", "blame"),
            new Hint("y", T("хеш", "hash"), Optional: true),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_list, BoxTitle.ForFocusable(_list, T($"История {Theme.SymbolSeparator} {path}", $"History {Theme.SymbolSeparator} {path}")),
            _details, _detailsTitle, _status, hints);
        _list.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _ = LoadAsync();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        // Blame файла в выбранном коммите (тем именем, какое у него было тогда).
        if (key == Key.B && _list.Selected is { Status: not 'D' } blamed)
        {
            using var blame = new BlameWindow(_git, blamed.Path, blamed.Commit.Hash);
            App!.Run(blame);
            return true;
        }

        if (key == Key.A)
        {
            _wholeCommit = !_wholeCommit;
            _ = LoadDetailsAsync();
            return true;
        }

        if (key == Key.Y && _list.Selected is { } revision)
        {
            SystemClipboard.SetText(revision.Commit.Hash);
            ShowStatus(T($"{Theme.SymbolSuccess} Хеш {revision.Commit.ShortHash} скопирован", $"{Theme.SymbolSuccess} Hash {revision.Commit.ShortHash} copied"), Theme.Success);
            return true;
        }

        if (key == Key.D.WithCtrl || key == Key.U.WithCtrl)
        {
            _details.ScrollBy((key == Key.D.WithCtrl ? 1 : -1) * Math.Max(1, _details.Viewport.Height / 2));
            return true;
        }

        return base.OnKeyDown(key);
    }

    private async Task LoadAsync()
    {
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Загрузка истории…", "Loading history…")}", Theme.Muted);
        try
        {
            var history = await _git.GetFileHistoryAsync(_path);
            App?.Invoke(() =>
            {
                var renames = history.Count(r => r.OldPath is not null);
                var limited = history.Count >= GitService.GraphLogLimit ? T($" (последние {GitService.GraphLogLimit})", $" (last {GitService.GraphLogLimit})") : "";
                ShowStatus(Plural(history.Count, "коммит", "коммита", "коммитов", "commit", "commits") + limited
                    + (renames > 0 ? $" {Theme.SymbolSeparator} {Plural(renames, "переименование", "переименования", "переименований", "rename", "renames")}" : ""),
                    Theme.Muted);
                _list.SetItems(history);
                _ = LoadDetailsAsync();
            });
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    /// <summary>Выбранный коммит: только этот файл или, после «a», все файлы коммита.</summary>
    private async Task LoadDetailsAsync()
    {
        _detailsLoading?.Cancel();
        if (_list.Selected is not { } revision)
        {
            _details.SetMessage("");
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        var scope = _wholeCommit ? T("весь коммит", "whole commit") : revision.Path;
        _detailsTitle.Text = $" {revision.Commit.ShortHash} {Theme.SymbolSeparator} {scope} ";
        try
        {
            var diff = _wholeCommit
                ? await _git.GetCommitDiffAsync(revision.Commit, loading.Token)
                : await _git.GetFileRevisionDiffAsync(revision, loading.Token);
            var prepared = await Task.Run(() => DiffPanel.PrepareCommit(revision.Commit, diff, DateTimeOffset.Now), loading.Token);
            App?.Invoke(() =>
            {
                if (!loading.IsCancellationRequested)
                    _details.SetDiff(prepared);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбран другой коммит.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}"));
        }
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
