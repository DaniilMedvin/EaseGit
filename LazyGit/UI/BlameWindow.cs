using LazyGit.Git;
using LazyGit.Models;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI;

/// <summary>
/// Blame файла: сверху каждая строка с коммитом, автором и давностью, снизу коммит выбранной строки (diff этого файла).
/// p — blame до этого коммита (кто написал строку раньше), esc — шаг назад или закрыть; a — весь коммит / файл;
/// y — хеш; ctrl+d/u — листать коммит.
/// </summary>
public sealed class BlameWindow : Runnable
{
    /// <summary>Что показано: файл в коммите (null — рабочая копия) и строка, на которой стоял курсор.</summary>
    private sealed record View(string Path, string? Revision, int Selected);

    private readonly GitService _git;
    private readonly Stack<View> _back = new();
    private readonly BlamePanel _lines;
    private readonly Label _linesTitle;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private View _view;
    private CancellationTokenSource? _detailsLoading;
    private string? _detailsFor;
    private bool _wholeCommit;

    /// <param name="revision">Коммит, состояние файла в котором показать; null — рабочая копия.</param>
    public BlameWindow(GitService git, string path, string? revision = null)
    {
        _git = git;
        _view = new View(path, revision, 0);
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _lines = new BlamePanel { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(60) };
        _lines.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        _linesTitle = BoxTitle.ForFocusable(_lines, "Blame");

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_lines), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, T("Коммит", "Commit"), Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("↑↓", T("строка", "line")),
            new Hint("p", T("до этого коммита", "before this commit")),
            new Hint("a", T("весь коммит / файл", "whole commit / file"), Optional: true),
            new Hint("y", T("хеш", "hash"), Optional: true),
            new Hint("ctrl+d/u", T("коммит", "commit"), Optional: true),
            new Hint("esc", T("назад", "back")),
        ]);

        Add(_lines, _linesTitle, _details, _detailsTitle, _status, hints);
        _lines.SetFocus();
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
            if (_back.TryPop(out var previous))
            {
                _view = previous;
                _ = LoadAsync();
            }
            else
            {
                RequestStop();
            }

            return true;
        }

        if (key == Key.P)
        {
            Before();
            return true;
        }

        if (key == Key.A)
        {
            _wholeCommit = !_wholeCommit;
            _detailsFor = null;
            _ = LoadDetailsAsync();
            return true;
        }

        if (key == Key.Y && _lines.Selected is { Commit.IsUncommitted: false } line)
        {
            SystemClipboard.SetText(line.Commit.Hash);
            ShowStatus(T($"{Theme.SymbolSuccess} Хеш {line.Commit.ShortHash} скопирован", $"{Theme.SymbolSuccess} Hash {line.Commit.ShortHash} copied"), Theme.Success);
            return true;
        }

        if (key == Key.D.WithCtrl || key == Key.U.WithCtrl)
        {
            _details.ScrollBy((key == Key.D.WithCtrl ? 1 : -1) * Math.Max(1, _details.Viewport.Height / 2));
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>Blame файла в родителе коммита выбранной строки: так видно, кто написал строку до этой правки.</summary>
    private void Before()
    {
        if (_lines.Selected is not { } line)
            return;

        if (line.Commit.IsUncommitted)
        {
            ShowStatus(T($"{Theme.SymbolError} Строка ещё не закоммичена: её прошлая версия — в последнем коммите (esc и B на файле)",
                $"{Theme.SymbolError} The line is not committed yet: its previous version is in the last commit"), Theme.Error);
            return;
        }

        if (line.Commit.PreviousHash is not { } parent || line.Commit.PreviousPath is not { } parentPath)
        {
            ShowStatus(T($"{Theme.SymbolError} Строка появилась в {line.Commit.ShortHash} вместе с файлом: раньше её не было",
                $"{Theme.SymbolError} The line appeared in {line.Commit.ShortHash} together with the file: it didn't exist before"), Theme.Error);
            return;
        }

        _back.Push(_view with { Selected = _lines.SelectedIndex });
        _view = new View(parentPath, parent, _lines.SelectedIndex);
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        var view = _view;
        var where = view.Revision is null ? T("рабочая копия", "working tree") : view.Revision[..Math.Min(7, view.Revision.Length)];
        _linesTitle.Text = $" Blame {Theme.SymbolSeparator} {view.Path} {Theme.SymbolSeparator} {where} ";
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Загрузка…", "Loading…")}", Theme.Muted);
        try
        {
            var lines = await _git.GetBlameAsync(view.Path, view.Revision);
            App?.Invoke(() =>
            {
                if (_view != view)
                    return;

                _lines.SetLines(lines, view.Path);
                _lines.SelectIndex(view.Selected);
                var commits = lines.Select(l => l.Commit.Hash).Distinct().Count();
                var depth = _back.Count > 0 ? T($" {Theme.SymbolSeparator} шаг назад: {_back.Count} (esc)", $" {Theme.SymbolSeparator} steps back: {_back.Count} (esc)") : "";
                ShowStatus(Plural(lines.Count, "строка", "строки", "строк", "line", "lines") + $" {Theme.SymbolSeparator} "
                    + Plural(commits, "коммит", "коммита", "коммитов", "commit", "commits") + depth, Theme.Muted);
                _detailsFor = null;
                _ = LoadDetailsAsync();
            });
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    /// <summary>Коммит выбранной строки; тот же, что уже показан, не перечитывается.</summary>
    private async Task LoadDetailsAsync()
    {
        if (_lines.Selected is not { } line || line.Commit.Hash == _detailsFor)
            return;

        _detailsLoading?.Cancel();
        _detailsFor = line.Commit.Hash;
        var commit = line.Commit;
        if (commit.IsUncommitted)
        {
            _detailsTitle.Text = T(" Не закоммичено ", " Uncommitted ");
            _details.SetMessage(T("Строка изменена в рабочей копии и ещё не закоммичена", "The line is changed in the working tree and not committed yet"));
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" {commit.ShortHash} {Theme.SymbolSeparator} {(_wholeCommit ? T("весь коммит", "whole commit") : commit.Path)} ";
        try
        {
            var full = commit.ToCommit();
            var diff = _wholeCommit
                ? await _git.GetCommitDiffAsync(full, loading.Token)
                : await _git.GetFileRevisionDiffAsync(new FileRevision(full, 'M', commit.Path), loading.Token);
            var prepared = await Task.Run(() => DiffPanel.PrepareCommit(full, diff, DateTimeOffset.Now), loading.Token);
            App?.Invoke(() =>
            {
                if (!loading.IsCancellationRequested)
                    _details.SetDiff(prepared);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбрана строка другого коммита.
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
