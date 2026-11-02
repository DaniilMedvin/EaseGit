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
/// Поиск по истории: поле запроса и режим (сообщение / автор / код), результаты по мере ввода, снизу выбранный коммит.
/// tab — режим, ctrl+a — все ветки, ↑↓ — коммит, ctrl+d/u — листать, y — хеш, esc — закрыть.
/// </summary>
public sealed class HistorySearchWindow : Runnable
{
    /// <summary>Пауза после ввода перед поиском: поиск по коду на большом репозитории не запускается на каждую букву.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(350);

    private static readonly HistorySearchMode[] Modes = [HistorySearchMode.Message, HistorySearchMode.Author, HistorySearchMode.Code];

    private readonly GitService _git;
    private readonly ClipboardTextField _query;
    private readonly View _modes;
    private readonly CommitsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private HistorySearchMode _mode = HistorySearchMode.Message;
    private bool _allBranches;
    private object? _timer;
    private CancellationTokenSource? _searching;
    private CancellationTokenSource? _detailsLoading;

    public HistorySearchWindow(GitService git)
    {
        _git = git;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        var box = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = 4, BorderStyle = LineStyle.Rounded, CanFocus = true };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);
        var prompt = new Label { Text = "> ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));
        // ctrl+a / d / u / y поле считает своими (выделить всё, удалить символ…) — здесь это клавиши окна,
        // поэтому окно получает их раньше поля.
        _query = new ClipboardTextField
        {
            X = Pos.Right(prompt),
            Y = 0,
            Width = Dim.Fill(),
            BeforeKey = HandleWindowKey,
        };
        _query.SetScheme(Theme.Input);
        _query.TextChanged += (_, _) => Schedule();
        _query.Accepting += (_, e) => e.Handled = true;
        _modes = new View { X = 0, Y = 1, Width = Dim.Fill(), Height = 1 };
        _modes.DrawingContent += (_, _) => _modes.DrawSegments(0, 0, ModeSegments(), _modes.Viewport.Width);
        box.Add(prompt, _query, _modes);

        _list = new CommitsPanel { X = 0, Y = Pos.Bottom(box), Width = Dim.Fill(), Height = Dim.Percent(35), ShowGraph = false, ShowAuthor = true, CanFocus = false };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, T("Коммит", "Commit"), Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("tab", T("где искать", "search in")),
            new Hint("ctrl+a", T("все ветки", "all branches")),
            new Hint("↑↓", T("коммит", "commit")),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("ctrl+y", T("хеш", "hash"), Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(box, BoxTitle.For(box, T("Поиск по истории", "Search history"), Theme.Accent), _list,
            BoxTitle.For(_list, T("Коммиты", "Commits"), Theme.Muted), _details, _detailsTitle, _status, hints);
        UpdateEmpty();
        _query.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (!newIsRunning)
            _searching?.Cancel();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.Tab || key == Key.Tab.WithShift)
        {
            var step = key == Key.Tab ? 1 : -1;
            _mode = Modes[(Array.IndexOf(Modes, _mode) + step + Modes.Length) % Modes.Length];
            _modes.SetNeedsDraw();
            UpdateEmpty();
            Schedule();
            return true;
        }

        // Стрелки двигают выбор в списке, пока фокус в поле запроса.
        if (key == Key.CursorDown || key == Key.CursorUp || key == Key.PageDown || key == Key.PageUp)
            return _list.NewKeyDownEvent(key);

        return HandleWindowKey(key) || base.OnKeyDown(key);
    }

    /// <summary>ctrl+a — все ветки, ctrl+d / ctrl+u — листать коммит, ctrl+y — скопировать хеш.</summary>
    private bool HandleWindowKey(Key key)
    {
        if (key == Key.A.WithCtrl)
            return ToggleAllBranches();

        if (key == Key.D.WithCtrl || key == Key.U.WithCtrl)
        {
            _details.ScrollBy((key == Key.D.WithCtrl ? 1 : -1) * Math.Max(1, _details.Viewport.Height / 2));
            return true;
        }

        if (key == Key.Y.WithCtrl)
        {
            if (_list.Selected is { } commit)
            {
                SystemClipboard.SetText(commit.Hash);
                ShowStatus(T($"{Theme.SymbolSuccess} Хеш {commit.ShortHash} скопирован", $"{Theme.SymbolSuccess} Hash {commit.ShortHash} copied"), Theme.Success);
            }

            return true;
        }

        return false;
    }

    private bool ToggleAllBranches()
    {
        _allBranches = !_allBranches;
        _modes.SetNeedsDraw();
        Schedule();
        return true;
    }

    /// <summary>«[Сообщение]  Автор  Код      текущая ветка»: режим — плашкой, справа где ищем.</summary>
    private IEnumerable<Segment> ModeSegments()
    {
        foreach (var mode in Modes)
        {
            var title = ModeTitle(mode);
            yield return mode == _mode ? Segment.Pill(title, Theme.Accent, TextStyle.Bold) : new Segment($" {title} ", Theme.Muted);
            yield return new Segment(" ", Theme.Dim);
        }

        yield return new Segment("   " + (_allBranches ? T("во всех ветках", "in all branches") : T("в текущей ветке", "in the current branch")),
            _allBranches ? Theme.Info : Theme.Dim);
    }

    private static string ModeTitle(HistorySearchMode mode) => mode switch
    {
        HistorySearchMode.Message => T("Сообщение", "Message"),
        HistorySearchMode.Author => T("Автор", "Author"),
        _ => T("Код", "Code"),
    };

    private void UpdateEmpty() =>
        _list.Empty = _query.Text.Trim().Length == 0
            ? _mode switch
            {
                HistorySearchMode.Message => T("Введи текст из сообщения коммита", "Type text from a commit message"),
                HistorySearchMode.Author => T("Введи имя или почту автора", "Type an author name or email"),
                _ => T("Введи строку кода: найдутся коммиты, где она появилась или исчезла", "Type a line of code: commits where it appeared or disappeared"),
            }
            : T("Ничего не найдено", "Nothing found");

    /// <summary>Поиск после паузы в вводе; предыдущий таймер и незаконченный поиск отменяются.</summary>
    private void Schedule()
    {
        UpdateEmpty();
        if (_timer is not null)
            App?.RemoveTimeout(_timer);
        _timer = App?.AddTimeout(Debounce, () =>
        {
            _timer = null;
            _ = SearchAsync();
            return false;
        });
    }

    private async Task SearchAsync()
    {
        _searching?.Cancel();
        var query = _query.Text.Trim();
        if (query.Length == 0)
        {
            _list.SetItems([]);
            _details.SetMessage("");
            ShowStatus("", Theme.Muted);
            return;
        }

        var searching = new CancellationTokenSource();
        _searching = searching;
        var (mode, all) = (_mode, _allBranches);
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Ищу…", "Searching…")}", Theme.Muted);
        try
        {
            var commits = await _git.SearchLogAsync(query, mode, all, searching.Token);
            App?.Invoke(() =>
            {
                if (searching.IsCancellationRequested)
                    return;

                _list.SetItems(commits);
                var limited = commits.Count >= GitService.GraphLogLimit ? T($" (первые {GitService.GraphLogLimit})", $" (first {GitService.GraphLogLimit})") : "";
                ShowStatus(Plural(commits.Count, "коммит найден", "коммита найдено", "коммитов найдено", "commit found", "commits found") + limited,
                    commits.Count > 0 ? Theme.Muted : Theme.Dim);
                _ = LoadDetailsAsync();
            });
        }
        catch (OperationCanceledException)
        {
            // Запрос изменился — ищем заново.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    private async Task LoadDetailsAsync()
    {
        _detailsLoading?.Cancel();
        if (_list.Selected is not { } commit)
        {
            _details.SetMessage("");
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" {commit.ShortHash} ";
        try
        {
            var diff = await _git.GetCommitDiffAsync(commit, loading.Token);
            var prepared = await Task.Run(() => DiffPanel.PrepareCommit(commit, diff, DateTimeOffset.Now), loading.Token);
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
