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
        _detailsTitle = BoxTitle.For(_details, T("Коммит", "Commit"), Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("space", "checkout"),
            new Hint("n", T("ветка отсюда", "branch here")),
            new Hint("t", T("тег", "tag")),
            new Hint("M", "merge", Optional: true),
            new Hint("C", "cherry-pick", Optional: true),
            new Hint("v", "revert", Optional: true),
            new Hint("=", T("сравнить с HEAD", "compare with HEAD"), Optional: true),
            new Hint("y", T("хеш", "hash"), Optional: true),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_list, BoxTitle.ForFocusable(_list, T("Граф · все ветки", "Graph · all branches")), _details, _detailsTitle, _status, hints);
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
        if (Commands.CommandKey.Char('=').Matches(key) && _list.Selected is { } compared)
        {
            using var compare = new CompareWindow(_git, compared.Hash, "HEAD");
            App!.Run(compare);
            return true;
        }

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
                    var limited = log.Count >= GitService.GraphLogLimit ? T($" (последние {GitService.GraphLogLimit})", $" (last {GitService.GraphLogLimit})") : "";
                    ShowStatus(T($"{PluralRu(log.Count, "коммит", "коммита", "коммитов")}{limited} ", $"{PluralEn(log.Count, "commit", "commits")}{limited} ") +
                        T($"{Theme.SymbolSeparator} {PluralRu(branches.Count, "локальная ветка", "локальные ветки", "локальных веток")}", $"{Theme.SymbolSeparator} {PluralEn(branches.Count, "local branch", "local branches")}"), Theme.Muted);
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
            _details.SetMessage(T("Коммитов пока нет", "No commits yet"));
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = T($" Коммит {Theme.SymbolSeparator} {commit.ShortHash} ", $" Commit {Theme.SymbolSeparator} {commit.ShortHash} ");
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
            ShowStatus(T($"{Theme.SymbolError} Ты уже на этом коммите", $"{Theme.SymbolError} You are already on this commit"), Theme.Error);
            return Task.CompletedTask;
        }

        var target = commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Branch);
        if (target is null && commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Remote) is { } remote)
        {
            var localName = remote.Name[(remote.Name.IndexOf('/') + 1)..];
            target = _branchNames.Contains(localName) ? new CommitRef(localName, RefKind.Branch) : remote;
        }

        var args = GitService.CheckoutArguments(target, commit);
        if (target is null && !Confirm(T($"Перейти на коммит {commit.ShortHash} без ветки (detached HEAD)?", $"Check out commit {commit.ShortHash} without a branch (detached HEAD)?"), args,
                T($"{Theme.SymbolWarning} Новые коммиты отсюда не попадут ни в одну ветку. Чтобы работать дальше, создай ветку: n.", $"{Theme.SymbolWarning} New commits from here won't belong to any branch. To keep working, create a branch: n."),
                T("Перейти", "Check out")))
            return Task.CompletedTask;

        var done = target switch
        {
            { Kind: RefKind.Remote } => T($"Создана ветка {target.Name[(target.Name.IndexOf('/') + 1)..]}, следит за {target.Name}", $"Created branch {target.Name[(target.Name.IndexOf('/') + 1)..]}, tracking {target.Name}"),
            { } => T($"Переключено на {target.Name}", $"Switched to {target.Name}"),
            _ => T($"HEAD на {commit.ShortHash} без ветки. Создать ветку: n", $"HEAD at {commit.ShortHash} without a branch. Create a branch: n"),
        };
        return RunAsync(ct => _git.CheckoutAsync(target, commit, ct), done);
    }

    private Task NewBranch()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        using var dialog = new InputDialog(T("Новая ветка", "New branch"), T($"От: {commit.ShortHash} {commit.Subject}", $"From: {commit.ShortHash} {commit.Subject}"), T("создать и переключиться", "create and switch"),
            name => BranchName.Check(name, _branchNames));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CreateBranchAtAsync(name, commit, ct), T($"Создана ветка {name} от {commit.ShortHash}, ты на ней", $"Created branch {name} at {commit.ShortHash}, you are on it"));
    }

    private Task NewTag()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        var tags = _list.Items.SelectMany(c => c.Refs).Where(r => r.Kind == RefKind.Tag).Select(r => r.Name).ToHashSet();
        using var dialog = new InputDialog(T("Новый тег", "New tag"), T($"На коммит: {commit.ShortHash} {commit.Subject}", $"On commit: {commit.ShortHash} {commit.Subject}"), T("создать", "create"),
            name => name.Length == 0 ? T("Введи имя тега, например v1.0", "Enter a tag name, e.g. v1.0")
                : tags.Contains(name) ? T($"Тег {name} уже существует", $"Tag {name} already exists")
                : BranchName.Check(name, []));
        App!.Run(dialog);
        if (dialog.Result is not { } name)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CreateTagAsync(name, commit, ct), T($"Тег {name} поставлен на {commit.ShortHash}. На сервер: git push origin {name}", $"Tag {name} set on {commit.ShortHash}. To the server: git push origin {name}"));
    }

    /// <summary>Вливает в текущую ветку ветку с этого коммита (локальную, иначе удалённую) или сам коммит.</summary>
    private Task MergeAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        if (IsHead(commit))
        {
            ShowStatus(T($"{Theme.SymbolError} Это текущий коммит: выбери ветку, которую нужно влить", $"{Theme.SymbolError} This is the current commit: pick a branch to merge"), Theme.Error);
            return Task.CompletedTask;
        }

        var revision = commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Branch)?.Name
            ?? commit.Refs.FirstOrDefault(r => r.Kind == RefKind.Remote)?.Name
            ?? commit.Hash;
        var name = revision == commit.Hash ? commit.ShortHash : revision;
        return RunAsync(ct => _git.MergeRevisionAsync(revision, ct), T($"{name} влит в текущую ветку", $"{name} merged into the current branch"));
    }

    private Task CherryPickAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CherryPickAsync(commit.Hash, ct), T($"Коммит {commit.ShortHash} скопирован в текущую ветку", $"Commit {commit.ShortHash} copied into the current branch"));
    }

    private Task RevertAsync()
    {
        if (_busy || _list.Selected is not { } commit)
            return Task.CompletedTask;

        if (!Confirm(T($"Откатить коммит {commit.ShortHash} «{commit.Subject}»?", $"Revert commit {commit.ShortHash} '{commit.Subject}'?"), GitService.RevertArguments(commit),
                T("В текущей ветке появится новый коммит с обратными изменениями. История не меняется.", "A new commit with the opposite changes will appear in the current branch. History is not changed."), "Revert"))
            return Task.CompletedTask;

        return RunAsync(ct => _git.RevertAsync(commit, ct), T($"Коммит {commit.ShortHash} откачен новым коммитом", $"Commit {commit.ShortHash} reverted with a new commit"));
    }

    private Task CopyHash()
    {
        if (_list.Selected is not { } commit)
            return Task.CompletedTask;

        SystemClipboard.SetText(commit.Hash);
        ShowStatus(T($"{Theme.SymbolSuccess} Хеш {commit.ShortHash} скопирован", $"{Theme.SymbolSuccess} Hash {commit.ShortHash} copied"), Theme.Success);
        return Task.CompletedTask;
    }

    /// <summary>Выполняет изменяющую команду, показывает итог и перечитывает граф. Одновременно — одна.</summary>
    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string success)
    {
        if (_busy)
            return;

        _busy = true;
        Result = true;
        ShowStatus(T($"{Theme.SpinnerFrames[0]} Выполняется…", $"{Theme.SpinnerFrames[0]} Running…"), Theme.Muted);
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
