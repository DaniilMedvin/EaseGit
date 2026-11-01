using LazyGit.Git;
using LazyGit.GitHub;
using LazyGit.Models;
using LazyGit.UI.Dialogs;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using CheckState = LazyGit.Models.CheckState;

namespace LazyGit.UI;

/// <summary>
/// Pull request на GitHub: вкладки «Этот репозиторий», «Мои PR», «Ждут меня»; сверху список, снизу выбранный PR
/// (состояние, описание, проверки CI, сводка файлов и diff с подсветкой). Над PR: checkout (только PR текущего
/// репозитория), открыть в браузере, merge, approve, скопировать адрес; n — новый PR из текущей ветки.
/// Result — что сделать главному окну после закрытия или null.
/// </summary>
public sealed class GitHubWindow : Runnable<GitHubAction?>
{
    private readonly GitHubService _github;
    private readonly GitHubRepo? _repo;
    private readonly IReadOnlyList<GitHubTab> _tabs;
    private readonly Dictionary<GitHubTab, IReadOnlyList<PullRequest>> _loaded = [];
    private readonly View _tabBar;
    private readonly PullRequestsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private readonly HintBar _hints;
    private GitHubTab _tab;
    private CancellationTokenSource? _listLoading;
    private CancellationTokenSource? _detailsLoading;

    // PR под курсором, загруженный целиком: по нему решается, можно ли влить и что предупредить.
    private PullRequest? _full;
    private bool _busy;

    /// <param name="repo">Репозиторий открытого проекта на GitHub или null: тогда только вкладки по всем репозиториям.</param>
    public GitHubWindow(GitHubService github, GitHubRepo? repo, GitHubTab tab)
    {
        _github = github;
        _repo = repo;
        _tabs = repo is null ? [GitHubTab.Mine, GitHubTab.ReviewRequested] : [GitHubTab.Repository, GitHubTab.Mine, GitHubTab.ReviewRequested];
        _tab = _tabs.Contains(tab) ? tab : _tabs[0];
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _tabBar = new View { X = 1, Y = 0, Width = Dim.Fill(1), Height = 1 };
        _tabBar.DrawingContent += (_, _) => _tabBar.DrawSegments(0, 0, _tabBarSegments, _tabBar.Viewport.Width);

        _list = new PullRequestsPanel { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Percent(40) };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, "PR", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        _hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };

        Add(_tabBar, _list, BoxTitle.ForFocusable(_list, repo?.NameWithOwner ?? "GitHub"), _details, _detailsTitle, _status, _hints);
        _list.SetFocus();
        UpdateTabs();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _ = LoadListAsync();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.Tab)
            return SwitchTab(+1);
        if (key == Key.Tab.WithShift)
            return SwitchTab(-1);
        Key[] numbers = [Key.D1, Key.D2, Key.D3];
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (key == numbers[i])
                return SelectTab(_tabs[i]);
        }

        if (key == Key.Space)
            return Handled(CheckoutAsync());
        if (key == Key.O)
            return Handled(OpenInBrowserAsync());
        if (key == Key.M.WithShift)
            return Handled(MergeAsync());
        if (key == Key.A)
            return Handled(ApproveAsync());
        if (key == Key.Y)
            return Handled(CopyUrl());
        if (key == Key.N && _repo is not null)
        {
            Result = GitHubAction.CreatePullRequest;
            RequestStop();
            return true;
        }

        if (key == Key.R)
        {
            _loaded.Remove(_tab);
            return Handled(LoadListAsync());
        }

        if (key == Key.D.WithCtrl)
            return Handled(ScrollDetails(+1));
        if (key == Key.U.WithCtrl)
            return Handled(ScrollDetails(-1));

        return base.OnKeyDown(key);
    }

    private static bool Handled(Task _) => true;

    private Task ScrollDetails(int direction)
    {
        _details.ScrollBy(direction * Math.Max(1, _details.Viewport.Height / 2));
        return Task.CompletedTask;
    }

    private bool SwitchTab(int step) => SelectTab(_tabs[(_tabs.ToList().IndexOf(_tab) + step + _tabs.Count) % _tabs.Count]);

    private bool SelectTab(GitHubTab tab)
    {
        if (tab == _tab)
            return true;

        _tab = tab;
        UpdateTabs();
        _ = LoadListAsync();
        return true;
    }

    private static string TabTitle(GitHubTab tab) => tab switch
    {
        GitHubTab.Repository => T("Этот репозиторий", "This repository"),
        GitHubTab.Mine => T("Мои PR", "My PRs"),
        _ => T("Ждут меня", "Review requested"),
    };

    /// <summary>Строка вкладок «1 Этот репозиторий 3   2 Мои PR 5   3 Ждут меня»: текущая — плашкой, число — после загрузки.</summary>
    private void UpdateTabs()
    {
        var segments = new List<Segment>();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var count = _loaded.TryGetValue(tab, out var prs) ? $" {prs.Count}" : "";
            var text = $"{i + 1} {TabTitle(tab)}{count}";
            segments.Add(tab == _tab ? Segment.Pill(text, Theme.Accent, TextStyle.Bold) : new Segment($" {text} ", Theme.Muted));
            segments.Add(new Segment("  ", Theme.Dim));
        }

        _tabBarSegments = segments;
        _tabBar.SetNeedsDraw();

        _list.ShowRepo = _tab != GitHubTab.Repository;
        _list.Empty = _tab switch
        {
            GitHubTab.Repository => T("Открытых PR нет. n — создать из текущей ветки", "No open PRs. n creates one from the current branch"),
            GitHubTab.Mine => T("У тебя нет открытых PR", "You have no open PRs"),
            _ => T("Никто не ждёт твоего ревью", "Nobody is waiting for your review"),
        };
        UpdateHints();
    }

    private IReadOnlyList<Segment> _tabBarSegments = [];

    private void UpdateHints()
    {
        var local = _list.Selected is { } pr && pr.Repo == _repo;
        var hints = new List<Hint> { new("tab", T("вкладка", "tab")) };
        if (local)
            hints.Add(new Hint("space", "checkout"));
        hints.Add(new Hint("o", T("браузер", "browser")));
        hints.Add(new Hint("M", "merge", Optional: true));
        hints.Add(new Hint("a", "approve", Optional: true));
        if (_repo is not null)
            hints.Add(new Hint("n", T("новый PR", "new PR")));
        hints.Add(new Hint("y", T("адрес", "link"), Optional: true));
        hints.Add(new Hint("r", T("обновить", "refresh"), Optional: true));
        hints.Add(new Hint("esc", T("закрыть", "close")));
        _hints.SetHints(hints);
    }

    private async Task LoadListAsync()
    {
        _listLoading?.Cancel();
        var tab = _tab;
        if (_loaded.TryGetValue(tab, out var cached))
        {
            _list.SetItems(cached);
            _ = LoadDetailsAsync();
            return;
        }

        var loading = new CancellationTokenSource();
        _listLoading = loading;
        _list.SetItems([]);
        _details.SetMessage("");
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Загрузка PR с GitHub…", "Loading PRs from GitHub…")}", Theme.Muted);
        try
        {
            var prs = tab switch
            {
                GitHubTab.Repository => await _github.ListAsync(_repo!, loading.Token),
                GitHubTab.Mine => await _github.SearchAsync(reviewRequested: false, loading.Token),
                _ => await _github.SearchAsync(reviewRequested: true, loading.Token),
            };
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _loaded[tab] = prs;
                ShowStatus("", Theme.Muted);
                UpdateTabs();
                _list.SetItems(prs);
                _ = LoadDetailsAsync();
            });
        }
        catch (OperationCanceledException)
        {
            // Выбрана другая вкладка.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}", Theme.Error));
        }
    }

    /// <summary>Выбранный PR целиком и его diff; параллельно, предыдущая загрузка отменяется.</summary>
    private async Task LoadDetailsAsync()
    {
        _detailsLoading?.Cancel();
        _full = null;
        UpdateHints();
        if (_list.Selected is not { } pr)
        {
            _detailsTitle.Text = " PR ";
            _details.SetMessage(_list.Items.Count == 0 ? "" : T("Выбери PR", "Choose a PR"));
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" {pr.Repo.Name}#{pr.Number} ";
        _details.SetMessage($"{Theme.SpinnerFrames[0]} {T("Загрузка…", "Loading…")}");
        try
        {
            var fullTask = _github.GetAsync(pr, loading.Token);
            var diffTask = _github.GetDiffAsync(pr, loading.Token);
            var full = await fullTask ?? pr;
            var diff = await diffTask;
            var prepared = await Task.Run(() => DiffPanel.PreparePullRequest(full, diff, DateTimeOffset.Now), loading.Token);
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _full = full;
                _details.SetDiff(prepared);
            });
        }
        catch (OperationCanceledException)
        {
            // Выбран другой PR.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}"));
        }
    }

    /// <summary>Переключает открытый проект на ветку PR. Только для PR этого же репозитория.</summary>
    private Task CheckoutAsync()
    {
        if (_list.Selected is not { } pr)
            return Task.CompletedTask;

        if (pr.Repo != _repo)
        {
            ShowStatus(T($"{Theme.SymbolError} PR из другого репозитория: открой его проект в LGit или в браузере (o)",
                $"{Theme.SymbolError} The PR is from another repository: open its project in LGit or in the browser (o)"), Theme.Error);
            return Task.CompletedTask;
        }

        return RunAsync(ct => _github.CheckoutAsync(pr, ct),
            T($"Ветка PR #{pr.Number} получена, ты на ней", $"Checked out the branch of PR #{pr.Number}"), changesRepo: true);
    }

    private Task OpenInBrowserAsync()
    {
        if (_list.Selected is not { } pr)
            return Task.CompletedTask;

        ShowStatus(T($"{Theme.SymbolSuccess} Открываю {pr.Url}", $"{Theme.SymbolSuccess} Opening {pr.Url}"), Theme.Success);
        return _github.OpenInBrowserAsync(pr);
    }

    private Task CopyUrl()
    {
        if (_list.Selected is not { } pr)
            return Task.CompletedTask;

        SystemClipboard.SetText(pr.Url);
        ShowStatus(T($"{Theme.SymbolSuccess} Адрес скопирован: {pr.Url}", $"{Theme.SymbolSuccess} Link copied: {pr.Url}"), Theme.Success);
        return Task.CompletedTask;
    }

    /// <summary>Merge на GitHub: способ на выбор, затем подтверждение с предупреждениями о CI, ревью и конфликте.</summary>
    private Task MergeAsync()
    {
        if (_busy || _list.Selected is not { } pr)
            return Task.CompletedTask;

        var full = _full ?? pr;
        if (full.Mergeable == "CONFLICTING")
        {
            ShowStatus(T($"{Theme.SymbolError} PR конфликтует с {full.BaseRef}: разреши конфликт в ветке PR и сделай push",
                $"{Theme.SymbolError} The PR conflicts with {full.BaseRef}: resolve it in the PR branch and push"), Theme.Error);
            return Task.CompletedTask;
        }

        MergeMethod[] methods = [MergeMethod.Squash, MergeMethod.Merge, MergeMethod.Rebase];
        using var choice = new ChoiceDialog("Merge", T($"Как влить #{pr.Number} «{pr.Title}»?", $"How to merge #{pr.Number} '{pr.Title}'?"),
        [
            new Choice("Squash", T("Все коммиты PR склеятся в один — история основной ветки чистая", "All PR commits become one: a clean history of the base branch")),
            new Choice("Merge commit", T("Коммиты PR сохранятся, сверху merge-коммит", "PR commits are kept, plus a merge commit")),
            new Choice("Rebase", T("Коммиты PR встанут поверх основной ветки без merge-коммита", "PR commits go on top of the base branch without a merge commit")),
        ]);
        App!.Run(choice);
        if (choice.Result is not { } index)
            return Task.CompletedTask;

        var method = methods[index];
        var warnings = new List<string>();
        if (full.IsDraft)
            warnings.Add(T("Это черновик.", "This is a draft."));
        if (full.Checks == CheckState.Failure)
            warnings.Add(T($"{Theme.SymbolWarning} CI упал.", $"{Theme.SymbolWarning} CI failed."));
        else if (full.Checks == CheckState.Pending)
            warnings.Add(T("CI ещё идёт.", "CI is still running."));
        if (full.Review == ReviewState.ChangesRequested)
            warnings.Add(T($"{Theme.SymbolWarning} Ревьюер просит правки.", $"{Theme.SymbolWarning} A reviewer requested changes."));
        warnings.Add(T($"Изменения попадут в {full.BaseRef ?? "основную ветку"} на GitHub, отменить можно только revert.",
            $"Changes go into {full.BaseRef ?? "the base branch"} on GitHub; only a revert can undo it."));

        if (!Confirm(T($"Влить PR #{pr.Number}?", $"Merge PR #{pr.Number}?"), GitHubService.MergeArguments(pr, method),
                string.Join(' ', warnings), "Merge"))
            return Task.CompletedTask;

        return RunAsync(ct => _github.MergeAsync(pr, method, ct), T($"PR #{pr.Number} влит ({method.ToString().ToLowerInvariant()})",
            $"PR #{pr.Number} merged ({method.ToString().ToLowerInvariant()})"), reload: true);
    }

    private Task ApproveAsync()
    {
        if (_busy || _list.Selected is not { } pr)
            return Task.CompletedTask;

        if (!Confirm(T($"Одобрить PR #{pr.Number} «{pr.Title}»?", $"Approve PR #{pr.Number} '{pr.Title}'?"), GitHubService.ApproveArguments(pr),
                T("На GitHub появится твоё одобрение (approve) от твоего имени.", "Your approval will appear on GitHub under your name."), "Approve"))
            return Task.CompletedTask;

        return RunAsync(ct => _github.ApproveAsync(pr, ct), T($"PR #{pr.Number} одобрен", $"PR #{pr.Number} approved"), reload: true);
    }

    /// <summary>Выполняет действие gh и показывает итог. <paramref name="reload"/> — перечитать список вкладки.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string success, bool changesRepo = false, bool reload = false)
    {
        if (_busy)
            return;

        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Выполняется…", "Running…")}", Theme.Muted);
        try
        {
            var result = await action(CancellationToken.None);
            if (result.Success && changesRepo)
                Result = GitHubAction.Refresh;
            App?.Invoke(() =>
            {
                ShowStatus(result.Success ? $"{Theme.SymbolSuccess} {success}" : $"{Theme.SymbolError} {GitErrors.Explain(result)}",
                    result.Success ? Theme.Success : Theme.Error);
                if (result.Success && reload)
                {
                    _loaded.Clear();
                    _ = LoadListAsync();
                }
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Confirm(string question, string[] args, string warning, string confirmText)
    {
        using var dialog = new ConfirmDialog(question, CommandText.Format(args, "gh"), warning, confirmText);
        App!.Run(dialog);
        return dialog.Result == true;
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
