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
/// GitHub Actions: сверху запуски (текущей ветки или всех), снизу выбранный — jobs с шагами или лог упавших шагов.
/// b — ветка / все, l — jobs / лог упавших, R — перезапустить (упавшие или всё), x — отменить идущий, o — в браузере,
/// r — обновить, ctrl+d/u — листать, esc — закрыть. Пока что-то идёт, список обновляется сам.
/// </summary>
public sealed class ActionsWindow : Runnable
{
    private static readonly TimeSpan AutoRefresh = TimeSpan.FromSeconds(10);

    private readonly GitHubService _github;
    private readonly GitHubRepo _repo;
    private readonly string? _branch;
    private readonly string? _headSha;
    private readonly ActionsRunsPanel _list;
    private readonly Label _listTitle;
    private readonly SegmentsPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private bool _allBranches;
    private bool _showLog;
    private bool _busy;
    private bool _firstLoad = true;
    private object? _timer;
    private CancellationTokenSource? _detailsLoading;
    private long? _detailsFor;

    /// <param name="branch">Ветка по умолчанию (текущая или ветка PR); null — все.</param>
    /// <param name="headSha">Коммит, запуск которого выбрать при открытии (HEAD).</param>
    public ActionsWindow(GitHubService github, GitHubRepo repo, string? branch, string? headSha)
    {
        _github = github;
        _repo = repo;
        _branch = branch;
        _headSha = headSha;
        _allBranches = branch is null;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _list = new ActionsRunsPanel { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(40), HeadSha = headSha };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        _listTitle = BoxTitle.ForFocusable(_list, "Actions");
        _details = new SegmentsPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, "Jobs", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("l", T("лог упавших / jobs", "failed log / jobs")),
            new Hint("R", T("перезапустить", "rerun")),
            new Hint("x", T("отменить", "cancel"), Optional: true),
            new Hint("b", T("ветка / все", "branch / all"), Optional: true),
            new Hint("o", T("браузер", "browser"), Optional: true),
            new Hint("ctrl+d/u", T("листать", "scroll"), Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_list, _listTitle, _details, _detailsTitle, _status, hints);
        _list.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
        {
            _ = LoadAsync();
            // Пока что-то идёт — обновляться самому; когда всё закончено, таймер ничего не делает.
            _timer = App?.AddTimeout(AutoRefresh, () =>
            {
                if (!_busy && _list.Items.Any(r => r.State == CheckState.Pending))
                {
                    _detailsFor = null;
                    _ = LoadAsync();
                }

                return true;
            });
        }
        else if (_timer is not null)
        {
            App?.RemoveTimeout(_timer);
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.B && _branch is not null)
        {
            _allBranches = !_allBranches;
            _ = LoadAsync();
            return true;
        }

        if (key == Key.L)
        {
            _showLog = !_showLog;
            _detailsFor = null;
            _ = LoadDetailsAsync();
            return true;
        }

        if (key == Key.R.WithShift)
            return Handled(RerunAsync());
        if (key == Key.X)
            return Handled(CancelAsync());
        if (key == Key.R)
        {
            _detailsFor = null;
            return Handled(LoadAsync());
        }

        if (key == Key.O && _list.Selected is { } run)
        {
            ShowStatus(T($"{Theme.SymbolSuccess} Открываю {run.Url}", $"{Theme.SymbolSuccess} Opening {run.Url}"), Theme.Success);
            _ = _github.OpenRunInBrowserAsync(_repo, run.Id);
            return true;
        }

        if (key == Key.D.WithCtrl || key == Key.U.WithCtrl)
        {
            _details.ScrollBy((key == Key.D.WithCtrl ? 1 : -1) * Math.Max(1, _details.Viewport.Height / 2));
            return true;
        }

        return base.OnKeyDown(key);
    }

    private static bool Handled(Task _) => true;

    private async Task LoadAsync()
    {
        var branch = _allBranches ? null : _branch;
        _listTitle.Text = branch is null
            ? T($" Actions {Theme.SymbolSeparator} {_repo} {Theme.SymbolSeparator} все ветки ", $" Actions {Theme.SymbolSeparator} {_repo} {Theme.SymbolSeparator} all branches ")
            : $" Actions {Theme.SymbolSeparator} {_repo} {Theme.SymbolSeparator} {Theme.SymbolBranch} {branch} ";
        if (_firstLoad)
            ShowStatus($"{Theme.SpinnerFrames[0]} {T("Загрузка запусков…", "Loading runs…")}", Theme.Muted);
        try
        {
            var runs = await _github.ListRunsAsync(_repo, branch);
            App?.Invoke(() =>
            {
                _list.SetItems(runs);
                // При открытии — запуск текущего коммита (HEAD), если он есть.
                if (_firstLoad && _headSha is not null && runs.ToList().FindIndex(r => r.Sha == _headSha) is var head and > 0)
                    _list.SelectIndex(head);
                _firstLoad = false;
                var running = runs.Count(r => r.State == CheckState.Pending);
                if (!_busy)
                    ShowStatus(Plural(runs.Count, "запуск", "запуска", "запусков", "run", "runs")
                        + (running > 0 ? T($" {Theme.SymbolSeparator} идут: {running} (обновляется само)", $" {Theme.SymbolSeparator} running: {running} (auto-refresh)") : ""), Theme.Muted);
                _ = LoadDetailsAsync();
            });
        }
        catch (Exception ex) when (ex is GitCommandException or System.Text.Json.JsonException)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {(ex is GitCommandException git ? GitErrors.Explain(git.Result) : ex.Message)}", Theme.Error));
        }
    }

    /// <summary>Jobs с шагами или (после l) лог упавших шагов выбранного запуска; тот же запуск не перечитывается.</summary>
    private async Task LoadDetailsAsync()
    {
        if (_list.Selected is not { } run || run.Id == _detailsFor)
            return;

        _detailsLoading?.Cancel();
        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        var keepScroll = _detailsFor == run.Id;
        _detailsFor = run.Id;
        _detailsTitle.Text = _showLog
            ? T($" Лог упавших шагов {Theme.SymbolSeparator} #{run.Number} ", $" Failed steps log {Theme.SymbolSeparator} #{run.Number} ")
            : $" Jobs {Theme.SymbolSeparator} #{run.Number} ";
        try
        {
            if (_showLog)
            {
                var log = await _github.GetFailedLogAsync(_repo, run.Id, loading.Token);
                App?.Invoke(() =>
                {
                    if (loading.IsCancellationRequested)
                        return;
                    if (log.Count == 0)
                        _details.SetMessage(run.State == CheckState.Pending
                            ? T("Запуск ещё идёт: лог будет, когда он закончится", "The run is still going: the log appears when it ends")
                            : T("Упавших шагов нет (или лог уже удалён GitHub по сроку)", "No failed steps (or GitHub already deleted the log)"));
                    else
                        _details.SetLines(LogLines(log), keepScroll);
                });
            }
            else
            {
                var jobs = await _github.GetJobsAsync(_repo, run.Id, loading.Token);
                App?.Invoke(() =>
                {
                    if (!loading.IsCancellationRequested)
                        _details.SetLines(JobLines(run, jobs), keepScroll);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Выбран другой запуск.
        }
        catch (Exception ex) when (ex is GitCommandException or System.Text.Json.JsonException)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {(ex is GitCommandException git ? GitErrors.Explain(git.Result) : ex.Message)}"));
        }
    }

    /// <summary>Заголовок запуска и jobs: «✗ test  51 с», под ним шаги «✓ 1 Set up job», упавшие — красным.</summary>
    private static List<IReadOnlyList<Segment>> JobLines(ActionsRun run, IReadOnlyList<ActionsJob> jobs)
    {
        var (runSymbol, runColor) = ActionsRunsPanel.Symbol(run.State, run.Conclusion);
        var lines = new List<IReadOnlyList<Segment>>
        {
            new[] { new Segment($"{runSymbol} {run.Workflow} #{run.Number}  ", runColor, TextStyle.Bold), new Segment(run.Title, Theme.Fg, TextStyle.Bold) },
            new[]
            {
                new Segment($"{Theme.SymbolBranch} {run.Branch} {Theme.SymbolSeparator} {run.ShortSha} {Theme.SymbolSeparator} {run.Event} {Theme.SymbolSeparator} ", Theme.Muted),
                new Segment(run.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm"), Theme.Fg),
                new Segment($" ({RelativeTime.Ago(run.CreatedAt, DateTimeOffset.Now)})", Theme.Accent),
            },
            Array.Empty<Segment>(),
        };
        foreach (var job in jobs)
        {
            var (symbol, color) = ActionsRunsPanel.Symbol(job.State, job.Conclusion);
            var duration = job.Duration is { } d ? $"  {(d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes} {T("мин", "min")} {d.Seconds} {T("с", "s")}" : $"{d.Seconds} {T("с", "s")}")}" : "";
            lines.Add([new Segment($"{symbol} ", color, TextStyle.Bold), new Segment(job.Name, Theme.Fg, TextStyle.Bold), new Segment(duration, Theme.Dim)]);
            foreach (var step in job.Steps)
            {
                var (stepSymbol, stepColor) = ActionsRunsPanel.Symbol(step.State, step.Conclusion);
                var failed = step.State == CheckState.Failure && step.Conclusion != "cancelled";
                lines.Add([new Segment($"    {stepSymbol} ", stepColor), new Segment($"{step.Number,2} ", Theme.Dim),
                    new Segment(step.Name, failed ? Theme.Error : Theme.Muted, failed ? TextStyle.Bold : TextStyle.None)]);
            }

            lines.Add([]);
        }

        if (jobs.Any(j => j.State == CheckState.Failure))
            lines.Add([new Segment(T("l — лог упавших шагов здесь, R — перезапустить упавшие", "l shows the failed steps log here, R reruns the failed ones"), Theme.Info)]);
        return lines;
    }

    /// <summary>Лог по шагам: заголовок «job › шаг» при смене шага, ошибки — красным.</summary>
    private static List<IReadOnlyList<Segment>> LogLines(IReadOnlyList<ActionsLogLine> log)
    {
        var lines = new List<IReadOnlyList<Segment>>();
        string? current = null;
        foreach (var line in log)
        {
            var section = $"{line.Job} › {line.Step}";
            if (section != current)
            {
                if (current is not null)
                    lines.Add([]);
                lines.Add([new Segment(section, Theme.Accent, TextStyle.Bold)]);
                current = section;
            }

            var error = line.IsError || line.Text.Contains("error", StringComparison.OrdinalIgnoreCase) || line.Text.TrimStart().StartsWith("Failed ", StringComparison.Ordinal);
            lines.Add([new Segment(line.Text.Replace("\t", "    "), line.IsError ? Theme.Error : error ? Theme.Warning : Theme.Fg,
                line.IsError ? TextStyle.Bold : TextStyle.None)]);
        }

        return lines;
    }

    /// <summary>Перезапуск: только упавшие jobs (быстрее) или весь запуск — выбор, затем подтверждение.</summary>
    private Task RerunAsync()
    {
        if (_busy || _list.Selected is not { } run)
            return Task.CompletedTask;

        if (run.State == CheckState.Pending)
        {
            ShowStatus(T($"{Theme.SymbolError} Запуск ещё идёт: дождись или отмени (x)", $"{Theme.SymbolError} The run is still going: wait or cancel it (x)"), Theme.Error);
            return Task.CompletedTask;
        }

        var failedOnly = false;
        if (run.State == CheckState.Failure)
        {
            using var choice = new ChoiceDialog(T("Перезапуск", "Rerun"), T($"Перезапустить {run.Workflow} #{run.Number}?", $"Rerun {run.Workflow} #{run.Number}?"),
            [
                new Choice(T("Только упавшие jobs", "Only the failed jobs"), T("Быстрее: прошедшие не повторяются", "Faster: passed jobs are not repeated")),
                new Choice(T("Весь запуск", "The whole run"), T("Все jobs заново", "All jobs again")),
            ]);
            App!.Run(choice);
            if (choice.Result is not { } index)
                return Task.CompletedTask;
            failedOnly = index == 0;
        }

        using var confirm = new ConfirmDialog(T($"Перезапустить {run.Workflow} #{run.Number}?", $"Rerun {run.Workflow} #{run.Number}?"),
            CommandText.Format(GitHubService.RerunArguments(_repo, run.Id, failedOnly), "gh"),
            T("Запуск пойдёт заново на GitHub и потратит минуты Actions (для публичных репозиториев — бесплатно).",
                "The run starts again on GitHub and uses Actions minutes (free for public repositories)."), T("Перезапустить", "Rerun"));
        App!.Run(confirm);
        if (confirm.Result != true)
            return Task.CompletedTask;

        return RunAsync(ct => _github.RerunAsync(_repo, run.Id, failedOnly, ct),
            T($"{run.Workflow} #{run.Number} перезапущен, статус обновится сам", $"{run.Workflow} #{run.Number} restarted, the status refreshes itself"));
    }

    private Task CancelAsync()
    {
        if (_busy || _list.Selected is not { State: CheckState.Pending } run)
            return Task.CompletedTask;

        using var confirm = new ConfirmDialog(T($"Отменить {run.Workflow} #{run.Number}?", $"Cancel {run.Workflow} #{run.Number}?"),
            CommandText.Format(GitHubService.CancelRunArguments(_repo, run.Id), "gh"),
            T("Идущие jobs остановятся, результат будет «отменён».", "Running jobs stop, the result is 'cancelled'."), T("Отменить запуск", "Cancel run"));
        App!.Run(confirm);
        return confirm.Result == true
            ? RunAsync(ct => _github.CancelRunAsync(_repo, run.Id, ct), T($"{run.Workflow} #{run.Number} отменяется", $"{run.Workflow} #{run.Number} is being cancelled"))
            : Task.CompletedTask;
    }

    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string success)
    {
        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Выполняется…", "Running…")}", Theme.Muted);
        try
        {
            var result = await action(CancellationToken.None);
            App?.Invoke(() => ShowStatus(result.Success ? $"{Theme.SymbolSuccess} {success}" : $"{Theme.SymbolError} {GitErrors.Explain(result)}",
                result.Success ? Theme.Success : Theme.Error));
            _detailsFor = null;
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
