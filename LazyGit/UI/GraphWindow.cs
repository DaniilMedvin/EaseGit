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

/// <summary>
/// Полноэкранный граф всех веток (как Git Graph в Visual Studio): сверху история локальных и удалённых веток
/// и тегов, снизу выбранный коммит. Над коммитом можно сделать checkout, создать от него ветку или тег,
/// влить, скопировать (cherry-pick), откатить (revert). Клавиши окна не переназначаются, как и в других диалогах.
/// Result — true, если что-то менялось и главному окну надо обновиться.
/// </summary>
public sealed class GraphWindow : Runnable<bool>
{
    private readonly GitService _git;
    private readonly CommitsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private HashSet<string> _branchNames = [];
    private CancellationTokenSource? _detailsLoading;
    private bool _busy;

    public GraphWindow(GitService git)
    {
        _git = git;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _list = new CommitsPanel { ShowAuthor = true, X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(55) };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();

        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, "Коммит", Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("space", "checkout"),
            new Hint("n", "ветка отсюда"),
            new Hint("t", "тег"),
            new Hint("M", "merge", Optional: true),
            new Hint("C", "cherry-pick", Optional: true),
            new Hint("v", "revert", Optional: true),
            new Hint("y", "хеш", Optional: true),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("esc", "закрыть"),
        ]);

        Add(_list, BoxTitle.ForFocusable(_list, "Граф · все ветки"), _details, _detailsTitle, _status, hints);
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

        if (key == Key.Space || key == Key.Enter)
            return Handled(CheckoutAsync());
        if (key == Key.N)
            return Handled(NewBranch());
        if (key == Key.T)
            return Handled(NewTag());
        if (key == Key.M.WithShift)
            return Handled(MergeAsync());
        if (key == Key.C.WithShift)
            return Handled(CherryPickAsync());
        if (key == Key.V)
            return Handled(RevertAsync());
        if (key == Key.Y)
            return Handled(CopyHash());
        if (key == Key.R)
            return Handled(LoadAsync());
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

    /// <summary>Перечитывает граф и список локальных веток (нужен для checkout удалённой ветки и проверки имени).</summary>
    private async Task LoadAsync()
    {
        try
        {
            var logTask = _git.GetLogAsync(GitService.GraphLogLimit, allBranches: true);
            var branchesTask = _git.GetBranchesAsync();
            var log = await logTask;
            var branches = await branchesTask;
            App?.Invoke(() =>
            {
                _branchNames = branches.Select(b => b.Name).ToHashSet();
                _list.SetItems(log);
                if (_status.Text.Length == 0)
                {
                    var limited = log.Count >= GitService.GraphLogLimit ? $" (последние {GitService.GraphLogLimit})" : "";
                    ShowStatus($"{TextDrawing.Plural(log.Count, "коммит", "коммита", "коммитов")}{limited} " +
                        $"{Theme.SymbolSeparator} {TextDrawing.Plural(branches.Count, "локальная ветка", "локальные ветки", "локальных веток")}", Theme.Muted);
                }

                _ = LoadDetailsAsync();
            });
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
            _details.SetMessage("Коммитов пока нет");
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" Коммит {Theme.SymbolSeparator} {commit.ShortHash} ";
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

    /// <summary>Коммит под курсором — текущий HEAD (на нём текущая ветка или detached HEAD).</summary>
    private static bool IsHead(Commit commit) =>
        commit.Refs.Any(r => r.IsCurrent || r.Kind == RefKind.Head);

    /// <summary>
    /// Checkout: локальная ветка на коммите — переключение на неё; только удалённая — локальная ветка с тем же
    /// именем (новая, следящая за удалённой, или уже существующая); без веток — detached HEAD после подтверждения.
    /// </summary>
    private Task CheckoutAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        if (IsHead(commit))
        {
            ShowStatus($"{Theme.SymbolError} Ты уже на этом коммите", Theme.Error);
            return Task.CompletedTask;
        }

        var target = commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Branch);
        if (target is null && commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Remote) is { } remote)
        {
            var localName = remote.Name[(remote.Name.IndexOf('/') + 1)..];
            target = _branchNames.Contains(localName) ? new CommitRef(localName, RefKind.Branch) : remote;
        }

        var args = GitService.CheckoutArguments(target, commit);
        if (target is null && !Confirm($"Перейти на коммит {commit.ShortHash} без ветки (detached HEAD)?", args,
                $"{Theme.SymbolWarning} Новые коммиты отсюда не попадут ни в одну ветку. Чтобы работать дальше, создай ветку: n.",
                "Перейти"))
            return Task.CompletedTask;

        var done = target switch
        {
            { Kind: RefKind.Remote } => $"Создана ветка {target.Name[(target.Name.IndexOf('/') + 1)..]}, следит за {target.Name}",
            { } => $"Переключено на {target.Name}",
            _ => $"HEAD на {commit.ShortHash} без ветки. Создать ветку: n",
        };
        return RunAsync(ct => _git.CheckoutAsync(target, commit, ct), done);
    }

    private Task NewBranch()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        using var dialog = new InputDialog("Новая ветка", $"От: {commit.ShortHash} {commit.Subject}", "создать и переключиться",
            name => BranchName.Check(name, _branchNames));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CreateBranchAtAsync(name, commit, ct), $"Создана ветка {name} от {commit.ShortHash}, ты на ней");
    }

    private Task NewTag()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        var tags = _list.Items.SelectMany(c => c.Refs).Where(r => r.Kind == RefKind.Tag).Select(r => r.Name).ToHashSet();
        using var dialog = new InputDialog("Новый тег", $"На коммит: {commit.ShortHash} {commit.Subject}", "создать",
            name => name.Length == 0 ? "Введи имя тега, например v1.0"
                : tags.Contains(name) ? $"Тег {name} уже существует"
                : BranchName.Check(name, []));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CreateTagAsync(name, commit, ct), $"Тег {name} поставлен на {commit.ShortHash}. На сервер: git push origin {name}");
    }

    /// <summary>Вливает в текущую ветку ветку с этого коммита (локальную, иначе удалённую) или сам коммит.</summary>
    private Task MergeAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        if (IsHead(commit))
        {
            ShowStatus($"{Theme.SymbolError} Это текущий коммит: выбери ветку, которую нужно влить", Theme.Error);
            return Task.CompletedTask;
        }

        var revision = commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Branch)?.Name
            ?? commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Remote)?.Name
            ?? commit.Hash;
        var name = revision == commit.Hash ? commit.ShortHash : revision;
        return RunAsync(ct => _git.MergeRevisionAsync(revision, ct), $"{name} влит в текущую ветку");
    }

    private Task CherryPickAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CherryPickAsync(commit.Hash, ct), $"Коммит {commit.ShortHash} скопирован в текущую ветку");
    }

    private Task RevertAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        if (!Confirm($"Откатить коммит {commit.ShortHash} «{commit.Subject}»?", GitService.RevertArguments(commit),
                "В текущей ветке появится новый коммит с обратными изменениями. История не меняется.", "Revert"))
            return Task.CompletedTask;

        return RunAsync(ct => _git.RevertAsync(commit, ct), $"Коммит {commit.ShortHash} откачен новым коммитом");
    }

    private Task CopyHash()
    {
        if (_list.Selected is not { } commit)
            return Task.CompletedTask;

        SystemClipboard.SetText(commit.Hash);
        ShowStatus($"{Theme.SymbolSuccess} Хеш {commit.ShortHash} скопирован", Theme.Success);
        return Task.CompletedTask;
    }

    /// <summary>Выполняет изменяющую команду, показывает итог и перечитывает граф. Одновременно — одна.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string success)
    {
        if (_busy)
            return;

        _busy = true;
        Result = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} Выполняется…", Theme.Muted);
        try
        {
            var result = await action(CancellationToken.None);
            App?.Invoke(() => ShowStatus(result.Success
                ? $"{Theme.SymbolSuccess} {success}"
                : $"{Theme.SymbolError} {GitErrors.Explain(result)}", result.Success ? Theme.Success : Theme.Error));
            await LoadAsync();
        }
        finally
        {
            _busy = false;
        }
    }

    private bool Confirm(string question, string[] args, string warning, string confirmText)
    {
        using var dialog = new ConfirmDialog(question, CommandText.Format(args), warning, confirmText);
        App!.Run(dialog);
        return dialog.Result == true;
    }

    private void ShowStatus(string text, Color color)
    {
        _status.Text = text;
        _status.SetScheme(Theme.Text(color));
    }
}
