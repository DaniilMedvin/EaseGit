using LazyGit.Commands;
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
/// репозитория), открыть в браузере, merge, approve, скопировать адрес; n — новый PR из текущей ветки;
/// enter на PR другого проекта — открыть его локальную копию или склонировать; / — выбрать любой репозиторий
/// и посмотреть (и влить) его PR в отдельной вкладке.
/// Result — что сделать главному окну после закрытия или null.
/// </summary>
public sealed class GitHubWindow : Runnable<GitHubAction?>
{
    private readonly GitHubService _github;
    private readonly GitHubRepo? _repo;
    private readonly LocalProjects _locals;
    private readonly Label _listTitle;

    // Репозиторий вкладки «Другой», выбранный через «/».
    private GitHubRepo? _other;
    private readonly string? _nearProject;
    private readonly List<GitHubTab> _tabs;
    private readonly Dictionary<GitHubTab, IReadOnlyList<PullRequest>> _loaded = [];
    private readonly View _tabBar;
    private readonly PullRequestsPanel _list;
    private readonly IssuesPanel _issues;
    private IReadOnlyList<Issue>? _loadedIssues;
    private Issue? _fullIssue;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private readonly HintBar _hints;
    private GitHubTab _tab;
    private CancellationTokenSource? _listLoading;
    private CancellationTokenSource? _detailsLoading;

    /// <summary>Путь репозитория для <see cref="GitHubAction.OpenRepository"/>.</summary>
    public string? OpenPath { get; private set; }

    // PR под курсором, загруженный целиком: по нему решается, можно ли влить и что предупредить.
    private PullRequest? _full;
    private bool _busy;

    /// <param name="repo">Репозиторий открытого проекта на GitHub или null: тогда только вкладки по всем репозиториям.</param>
    /// <param name="locals">Недавние проекты: в них ищутся локальные копии, они же первыми в выборе репозитория.</param>
    /// <param name="nearProject">Проект, рядом с которым клонировать новые (или null — в домашнюю папку).</param>
    public GitHubWindow(GitHubService github, GitHubRepo? repo, GitHubTab tab, LocalProjects locals, string? nearProject)
    {
        _github = github;
        _repo = repo;
        _locals = locals;
        _nearProject = nearProject;
        _tabs = repo is null
            ? [GitHubTab.Mine, GitHubTab.ReviewRequested]
            : [GitHubTab.Repository, GitHubTab.Issues, GitHubTab.Mine, GitHubTab.ReviewRequested];
        _tab = _tabs.Contains(tab) ? tab : _tabs[0];
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _tabBar = new View { X = 1, Y = 0, Width = Dim.Fill(1), Height = 1 };
        _tabBar.DrawingContent += (_, _) => _tabBar.DrawSegments(0, 0, _tabBarSegments, _tabBar.Viewport.Width);

        _list = new PullRequestsPanel { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Percent(40) };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        _issues = new IssuesPanel { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Percent(40), Visible = false };
        _issues.SelectionChanged += (_, _) => _ = LoadIssueAsync();

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, "PR", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        _hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };

        _listTitle = BoxTitle.ForFocusable(_list, repo?.NameWithOwner ?? "GitHub");
        Add(_tabBar, _list, _issues, _listTitle, _details, _detailsTitle, _status, _hints);
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

        if (CommandKey.Char('/').Matches(key))
            return Handled(PickRepository());
        if (key == Key.Tab)
            return SwitchTab(+1);
        if (key == Key.Tab.WithShift)
            return SwitchTab(-1);
        Key[] numbers = [Key.D1, Key.D2, Key.D3, Key.D4, Key.D5];
        for (var i = 0; i < _tabs.Count; i++)
        {
            if (key == numbers[i])
                return SelectTab(_tabs[i]);
        }

        if (_tab == GitHubTab.Issues)
        {
            if (key == Key.N)
                return Handled(CreateIssueAsync());
            if (key == Key.C)
                return Handled(CommentIssueAsync());
            if (key == Key.X)
                return Handled(CloseIssueAsync());
            if (key == Key.O && _issues.Selected is { } browse)
            {
                ShowStatus(T($"{Theme.SymbolSuccess} Открываю {browse.Url}", $"{Theme.SymbolSuccess} Opening {browse.Url}"), Theme.Success);
                _ = _github.OpenIssueInBrowserAsync(browse);
                return true;
            }

            if (key == Key.Y && _issues.Selected is { } copied)
            {
                SystemClipboard.SetText(copied.Url);
                ShowStatus(T($"{Theme.SymbolSuccess} Адрес скопирован: {copied.Url}", $"{Theme.SymbolSuccess} Link copied: {copied.Url}"), Theme.Success);
                return true;
            }

            if (key == Key.R)
            {
                _loadedIssues = null;
                return Handled(LoadListAsync());
            }

            if (key == Key.D.WithCtrl)
                return Handled(ScrollDetails(+1));
            if (key == Key.U.WithCtrl)
                return Handled(ScrollDetails(-1));
            return base.OnKeyDown(key);
        }

        if (key == Key.Space)
            return Handled(CheckoutAsync());
        if (key == Key.Enter)
            return Handled(OpenProjectAsync());
        if (key == Key.O)
            return Handled(OpenInBrowserAsync());
        if (key == Key.M.WithShift)
            return Handled(MergeAsync());
        if (key == Key.A)
            return Handled(ApproveAsync());
        if (key == Key.Y)
            return Handled(CopyUrl());
        if (key == Key.W && _list.Selected is { } prRun)
        {
            // Actions ветки PR: почему у него ✗ CI.
            using var actions = new ActionsWindow(_github, prRun.Repo, prRun.HeadRef ?? _full?.HeadRef, headSha: null);
            App!.Run(actions);
            return true;
        }
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

    private bool SwitchTab(int step) => SelectTab(_tabs[(_tabs.IndexOf(_tab) + step + _tabs.Count) % _tabs.Count]);

    /// <summary>
    /// «/» — выбор репозитория. Выбран открытый проект — вкладка «Этот репозиторий», иначе — вкладка этого репозитория
    /// (одна, заменяется при следующем выборе) со всеми его открытыми PR.
    /// </summary>
    private Task PickRepository()
    {
        using var picker = new RepoPickerDialog(async ct =>
        {
            var localsTask = _locals.ListAsync(ct);
            IReadOnlyList<RepoChoice> remote;
            try
            {
                remote = await _github.ListMyReposAsync(ct);
            }
            catch (Git.GitCommandException)
            {
                // Без сети — хотя бы недавние локальные и ручной ввод.
                remote = [];
            }

            return RepoChoices.Merge(await localsTask, remote);
        });
        App!.Run(picker);
        if (picker.Result is not { } chosen)
            return Task.CompletedTask;

        if (_repo is not null && LocalClones.SameRepo(chosen, _repo))
        {
            SelectTab(GitHubTab.Repository);
            return Task.CompletedTask;
        }

        _other = chosen;
        _loaded.Remove(GitHubTab.Other);
        _loadedIssues = null;
        if (!_tabs.Contains(GitHubTab.Other))
            _tabs.Add(GitHubTab.Other);
        // Issues — выбранного репозитория (на стартовом экране вкладка появляется только теперь).
        if (!_tabs.Contains(GitHubTab.Issues))
            _tabs.Add(GitHubTab.Issues);
        if (_tab == GitHubTab.Other)
        {
            UpdateTabs();
            return LoadListAsync();
        }

        SelectTab(GitHubTab.Other);
        return Task.CompletedTask;
    }

    private bool SelectTab(GitHubTab tab)
    {
        if (tab == _tab)
            return true;

        _tab = tab;
        UpdateTabs();
        _ = LoadListAsync();
        return true;
    }

    private string TabTitle(GitHubTab tab) => tab switch
    {
        GitHubTab.Repository => T("Этот репозиторий", "This repository"),
        GitHubTab.Mine => T("Мои PR", "My PRs"),
        GitHubTab.ReviewRequested => T("Ждут меня", "Review requested"),
        GitHubTab.Issues => "Issues",
        _ => _other?.NameWithOwner ?? "",
    };

    /// <summary>Репозиторий вкладки Issues: выбранный через «/», иначе открытый.</summary>
    private GitHubRepo? IssuesRepo => _other ?? _repo;

    /// <summary>Строка вкладок «1 Этот репозиторий 3   2 Мои PR 5   3 Ждут меня»: текущая — плашкой, число — после загрузки.</summary>
    private void UpdateTabs()
    {
        var segments = new List<Segment>();
        for (var i = 0; i < _tabs.Count; i++)
        {
            var tab = _tabs[i];
            var count = tab == GitHubTab.Issues ? _loadedIssues is { } loadedIssues ? $" {loadedIssues.Count}" : ""
                : _loaded.TryGetValue(tab, out var prs) ? $" {prs.Count}" : "";
            var text = $"{i + 1} {TabTitle(tab)}{count}";
            segments.Add(tab == _tab ? Segment.Pill(text, Theme.Accent, TextStyle.Bold) : new Segment($" {text} ", Theme.Muted));
            segments.Add(new Segment("  ", Theme.Dim));
        }

        _tabBarSegments = segments;
        _tabBar.SetNeedsDraw();

        _list.ShowRepo = _tab is GitHubTab.Mine or GitHubTab.ReviewRequested;
        _list.Visible = _tab != GitHubTab.Issues;
        _issues.Visible = _tab == GitHubTab.Issues;
        (_tab == GitHubTab.Issues ? (View)_issues : _list).SetFocus();
        _issues.Empty = T("Открытых issues нет. n — создать", "No open issues. n creates one");
        _listTitle.Text = $" {_tab switch
        {
            GitHubTab.Repository => _repo?.NameWithOwner,
            GitHubTab.Other => _other?.NameWithOwner,
            GitHubTab.Issues => $"Issues {Theme.SymbolSeparator} {IssuesRepo?.NameWithOwner}",
            _ => T("Все репозитории", "All repositories"),
        }} ";
        _list.Empty = _tab switch
        {
            GitHubTab.Repository => T("Открытых PR нет. n — создать из текущей ветки", "No open PRs. n creates one from the current branch"),
            GitHubTab.Mine => T("У тебя нет открытых PR. / — выбрать репозиторий", "You have no open PRs. / picks a repository"),
            GitHubTab.ReviewRequested => T("Никто не ждёт твоего ревью. / — выбрать репозиторий", "Nobody is waiting for your review. / picks a repository"),
            _ => T("В этом репозитории нет открытых PR", "This repository has no open PRs"),
        };
        UpdateHints();
    }

    private IReadOnlyList<Segment> _tabBarSegments = [];

    private void UpdateHints()
    {
        if (_tab == GitHubTab.Issues)
        {
            _hints.SetHints(
            [
                new Hint("tab", T("вкладка", "tab")),
                new Hint("n", T("новый issue", "new issue")),
                new Hint("c", T("комментарий", "comment")),
                new Hint("x", T("закрыть issue", "close issue")),
                new Hint("o", T("браузер", "browser"), Optional: true),
                new Hint("y", T("адрес", "link"), Optional: true),
                new Hint("esc", T("закрыть", "close")),
            ]);
            return;
        }

        var local = _list.Selected is { } pr && IsCurrentRepo(pr);
        var hints = new List<Hint> { new("tab", T("вкладка", "tab")), new("/", T("репозиторий", "repository")) };
        if (local)
            hints.Add(new Hint("space", "checkout"));
        else if (_list.Selected is not null)
            hints.Add(new Hint("enter", T("открыть проект", "open project")));
        hints.Add(new Hint("o", T("браузер", "browser")));
        hints.Add(new Hint("w", "Actions", Optional: true));
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
        if (tab == GitHubTab.Issues)
        {
            await LoadIssuesAsync();
            return;
        }

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
                GitHubTab.ReviewRequested => await _github.SearchAsync(reviewRequested: true, loading.Token),
                _ => await _github.ListAsync(_other!, loading.Token),
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

    private async Task LoadIssuesAsync()
    {
        if (IssuesRepo is not { } repo)
            return;

        if (_loadedIssues is { } cached)
        {
            _issues.SetItems(cached);
            _ = LoadIssueAsync();
            return;
        }

        var loading = new CancellationTokenSource();
        _listLoading = loading;
        _issues.SetItems([]);
        _details.SetMessage("");
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Загрузка issues…", "Loading issues…")}", Theme.Muted);
        try
        {
            var issues = await _github.ListIssuesAsync(repo, loading.Token);
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _loadedIssues = issues;
                ShowStatus("", Theme.Muted);
                UpdateTabs();
                _issues.SetItems(issues);
                _ = LoadIssueAsync();
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

    /// <summary>Выбранный issue целиком: описание и комментарии.</summary>
    private async Task LoadIssueAsync()
    {
        _detailsLoading?.Cancel();
        _fullIssue = null;
        if (_issues.Selected is not { } issue)
        {
            _detailsTitle.Text = " Issue ";
            _details.SetMessage("");
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" #{issue.Number} ";
        _details.SetMessage($"{Theme.SpinnerFrames[0]} {T("Загрузка…", "Loading…")}");
        try
        {
            var full = await _github.GetIssueAsync(issue, loading.Token) ?? issue;
            App?.Invoke(() =>
            {
                if (loading.IsCancellationRequested)
                    return;

                _fullIssue = full;
                _details.SetDiff(DiffPanel.PrepareIssue(full, DateTimeOffset.Now));
            });
        }
        catch (OperationCanceledException)
        {
            // Выбран другой issue.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}"));
        }
    }

    private Task CreateIssueAsync()
    {
        if (_busy || IssuesRepo is not { } repo)
            return Task.CompletedTask;

        using var dialog = new IssueDialog(T($"Новый issue · {repo}", $"New issue · {repo}"),
            T("Опиши задачу или ошибку: что ожидалось и что получилось", "Describe the task or bug: what was expected and what happened"), withTitle: true);
        App!.Run(dialog);
        if (dialog.Result is not { Title: { } title } text)
            return Task.CompletedTask;

        return RunIssueAsync(ct => _github.CreateIssueAsync(repo, title, text.Body, ct),
            result => T($"Issue создан: {result.StdOut.Trim().Split('\n').LastOrDefault()}", $"Issue created: {result.StdOut.Trim().Split('\n').LastOrDefault()}"));
    }

    private Task CommentIssueAsync()
    {
        if (_busy || _issues.Selected is not { } issue)
            return Task.CompletedTask;

        using var dialog = new IssueDialog(T($"Комментарий к #{issue.Number}", $"Comment on #{issue.Number}"), issue.Title, withTitle: false);
        App!.Run(dialog);
        if (dialog.Result is not { } text)
            return Task.CompletedTask;

        return RunIssueAsync(ct => _github.CommentIssueAsync(issue, text.Body, ct), _ => T($"Комментарий к #{issue.Number} отправлен", $"Comment on #{issue.Number} posted"));
    }

    private Task CloseIssueAsync()
    {
        if (_busy || _issues.Selected is not { } issue)
            return Task.CompletedTask;

        if (!Confirm(T($"Закрыть issue #{issue.Number} «{issue.Title}»?", $"Close issue #{issue.Number} '{issue.Title}'?"),
                GitHubService.CloseIssueArguments(issue),
                T("Issue закроется на GitHub; открыть заново можно там же.", "The issue is closed on GitHub; it can be reopened there."), T("Закрыть", "Close")))
            return Task.CompletedTask;

        return RunIssueAsync(ct => _github.CloseIssueAsync(issue, ct), _ => T($"Issue #{issue.Number} закрыт", $"Issue #{issue.Number} closed"));
    }

    /// <summary>Действие с issue через gh; после успеха список issues перечитывается.</summary>
    private async Task RunIssueAsync(Func<CancellationToken, Task<GitResult>> action, Func<GitResult, string> success)
    {
        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Выполняется…", "Running…")}", Theme.Muted);
        try
        {
            var result = await action(CancellationToken.None);
            App?.Invoke(() =>
            {
                ShowStatus(result.Success ? $"{Theme.SymbolSuccess} {success(result)}" : $"{Theme.SymbolError} {GitErrors.Explain(result)}",
                    result.Success ? Theme.Success : Theme.Error);
                if (result.Success)
                {
                    _loadedIssues = null;
                    _ = LoadIssuesAsync();
                }
            });
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Переключает открытый проект на ветку PR. Только для PR этого же репозитория.</summary>
    private Task CheckoutAsync()
    {
        if (_list.Selected is not { } pr)
            return Task.CompletedTask;

        if (!IsCurrentRepo(pr))
        {
            ShowStatus(T($"{Theme.SymbolError} PR из другого репозитория: открой его проект в LGit или в браузере (o)",
                $"{Theme.SymbolError} The PR is from another repository: open its project in LGit or in the browser (o)"), Theme.Error);
            return Task.CompletedTask;
        }

        return RunAsync(ct => _github.CheckoutAsync(pr, ct),
            T($"Ветка PR #{pr.Number} получена, ты на ней", $"Checked out the branch of PR #{pr.Number}"), changesRepo: true);
    }

    private bool IsCurrentRepo(PullRequest pr) => _repo is not null && LocalClones.SameRepo(pr.Repo, _repo);

    /// <summary>
    /// Переход в проект PR: локальная копия среди недавно открытых — открывается, нет — предложение
    /// склонировать рядом с текущим проектом. PR этого же репозитория — подсказка про checkout.
    /// </summary>
    private async Task OpenProjectAsync()
    {
        if (_busy || _list.Selected is not { } pr)
            return;

        if (IsCurrentRepo(pr))
        {
            ShowStatus(T($"{Theme.SymbolSuccess} Это открытый сейчас проект: space — перейти на ветку PR",
                $"{Theme.SymbolSuccess} This is the open project: space checks out the PR branch"), Theme.Muted);
            return;
        }

        var local = await _locals.FindAsync(pr.Repo);
        App?.Invoke(() =>
        {
            if (local is not null)
            {
                Open(local);
                return;
            }

            var path = LocalClones.ClonePath(_nearProject, pr.Repo);
            if (!Confirm(T($"Склонировать {pr.Repo}?", $"Clone {pr.Repo}?"), GitHubService.CloneArguments(pr.Repo, path),
                    T($"Локальной копии среди недавних проектов нет. Репозиторий скачается в {path} и откроется в LGit.",
                        $"There is no local copy among recent projects. The repository will be downloaded to {path} and opened in LGit."),
                    T("Клонировать", "Clone")))
                return;

            _ = CloneAndOpenAsync(pr.Repo, path);
        });
    }

    private async Task CloneAndOpenAsync(GitHubRepo repo, string path)
    {
        if (_busy)
            return;

        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T($"Клонирую {repo} в {path}…", $"Cloning {repo} into {path}…")}", Theme.Muted);
        try
        {
            var result = await _github.CloneAsync(repo, path);
            App?.Invoke(() =>
            {
                if (result.Success)
                    Open(path);
                else
                    ShowStatus($"{Theme.SymbolError} {GitErrors.Explain(result)}", Theme.Error);
            });
        }
        finally
        {
            _busy = false;
        }
    }

    private void Open(string path)
    {
        OpenPath = path;
        Result = GitHubAction.OpenRepository;
        RequestStop();
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
