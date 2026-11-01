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
/// Worktrees — несколько рабочих папок одного репозитория, в каждой своя ветка. enter — открыть в LGit, n — новый
/// (ветка и папка), D — удалить, p — убрать записи о пропавших папках, y — путь, esc — закрыть.
/// <see cref="OpenPath"/> — какую папку открыть после закрытия (или null).
/// </summary>
public sealed class WorktreesWindow : Runnable
{
    private readonly GitService _git;
    private readonly WorktreesPanel _list;
    private readonly Label _status;
    private bool _busy;

    public WorktreesWindow(GitService git)
    {
        _git = git;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(85);
        Height = Dim.Percent(60);
        SetScheme(Theme.Base);

        _list = new WorktreesPanel { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(2), Current = git.RepoRoot };
        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("enter", T("открыть", "open")),
            new Hint("n", T("новый", "new")),
            new Hint("D", T("удалить", "delete")),
            new Hint("p", T("убрать пропавшие", "prune"), Optional: true),
            new Hint("y", T("путь", "path"), Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_list, BoxTitle.ForFocusable(_list, "Worktrees"), _status, hints);
        _list.SetFocus();
    }

    /// <summary>Папка worktree, которую открыть в LGit после закрытия окна.</summary>
    public string? OpenPath { get; private set; }

    /// <summary>Что-то изменилось (создан или удалён worktree): главному окну стоит обновиться.</summary>
    public bool Changed { get; private set; }

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

        if (key == Key.Enter && _list.Selected is { } open)
        {
            if (open.Path == _git.RepoRoot)
                ShowStatus(T($"{Theme.SymbolSuccess} Этот worktree уже открыт", $"{Theme.SymbolSuccess} This worktree is already open"), Theme.Muted);
            else if (open.Prunable is not null)
                ShowStatus(T($"{Theme.SymbolError} Папки нет: p — убрать запись о ней", $"{Theme.SymbolError} The folder is gone: p removes its entry"), Theme.Error);
            else
            {
                OpenPath = open.Path;
                RequestStop();
            }

            return true;
        }

        if (key == Key.N)
            return Handled(CreateAsync());
        if (key == Key.D.WithShift)
            return Handled(DeleteAsync());
        if (key == Key.P)
            return Handled(RunAsync(ct => _git.PruneWorktreesAsync(ct), T("Записи о пропавших папках убраны", "Entries of missing folders removed")));
        if (key == Key.Y && _list.Selected is { } copy)
        {
            SystemClipboard.SetText(copy.Path);
            ShowStatus(T($"{Theme.SymbolSuccess} Путь скопирован", $"{Theme.SymbolSuccess} Path copied"), Theme.Success);
            return true;
        }

        return base.OnKeyDown(key);
    }

    private static bool Handled(Task _) => true;

    private async Task LoadAsync()
    {
        try
        {
            var worktrees = await _git.GetWorktreesAsync();
            var changes = new Dictionary<string, int?>();
            foreach (var worktree in worktrees.Where(w => !w.IsBare))
                changes[worktree.Path] = await _git.CountChangesAsync(worktree.Path);
            App?.Invoke(() =>
            {
                _list.Changes = changes;
                _list.SetItems(worktrees);
                _list.SetNeedsDraw();
                if (!_busy)
                    ShowStatus(worktrees.Count == 1
                        ? T("Только основная папка. n — ещё одна рабочая папка для другой ветки", "Only the main folder. n adds a working folder for another branch")
                        : Plural(worktrees.Count, "рабочая папка", "рабочие папки", "рабочих папок", "working folder", "working folders"), Theme.Muted);
            });
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
        }
    }

    /// <summary>
    /// Новый worktree: ветка (есть — её checkout в новой папке, нет — новая от HEAD), затем папка — по умолчанию рядом
    /// с основной: «проект-ветка».
    /// </summary>
    private async Task CreateAsync()
    {
        if (_busy)
            return;

        var branches = await _git.GetBranchesAsync();
        var busy = _list.Items.Where(w => w.Branch is not null).ToDictionary(w => w.Branch!, w => w.Path);
        App?.Invoke(() =>
        {
            var names = branches.Select(b => b.Name).ToHashSet();
            using var branchDialog = new InputDialog(T("Новый worktree", "New worktree"),
                T("Ветка: существующая откроется в новой папке, новая создастся от HEAD", "Branch: an existing one opens in the new folder, a new one is created from HEAD"),
                T("дальше", "next"), name =>
                    busy.TryGetValue(name, out var where) ? T($"Ветка уже открыта в {TextDrawing.ShortenHome(where)}", $"The branch is already open in {TextDrawing.ShortenHome(where)}")
                    : names.Contains(name) ? null
                    : BranchName.Check(name, []));
            App!.Run(branchDialog);
            if (branchDialog.Result is not { } branch)
                return;

            var main = _list.Items.FirstOrDefault(w => w.IsMain)?.Path ?? _git.RepoRoot;
            var suggested = Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(main)) ?? main,
                $"{Path.GetFileName(Path.TrimEndingDirectorySeparator(main))}-{branch.Replace('/', '-')}");
            var isNew = !names.Contains(branch);
            using var pathDialog = new InputDialog(T("Папка worktree", "Worktree folder"),
                isNew ? T($"Новая ветка {branch} от HEAD", $"New branch {branch} from HEAD") : T($"Ветка {branch}", $"Branch {branch}"),
                T("создать", "create"), path => Directory.Exists(path) && Directory.EnumerateFileSystemEntries(path).Any()
                    ? T("Папка уже есть и не пуста", "The folder exists and is not empty") : null, initial: TextDrawing.ShortenHome(suggested));
            App!.Run(pathDialog);
            if (pathDialog.Result is not { } entered)
                return;

            var path = entered.StartsWith('~') ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + entered[1..] : Path.GetFullPath(entered);
            _ = RunAsync(ct => _git.AddWorktreeAsync(path, branch, isNew, ct),
                T($"Worktree {TextDrawing.ShortenHome(path)} создан: enter — открыть", $"Worktree {TextDrawing.ShortenHome(path)} created: enter opens it"));
        });
    }

    /// <summary>Удаление: подтверждение; есть изменения — git откажет, тогда второе подтверждение с --force.</summary>
    private async Task DeleteAsync()
    {
        if (_busy || _list.Selected is not { } worktree)
            return;

        if (worktree.IsMain || worktree.Path == _git.RepoRoot)
        {
            ShowStatus(worktree.IsMain
                ? T($"{Theme.SymbolError} Основную папку удалить нельзя", $"{Theme.SymbolError} The main folder can't be removed")
                : T($"{Theme.SymbolError} Этот worktree сейчас открыт: открой другой и удали отсюда", $"{Theme.SymbolError} This worktree is open now: open another one and delete it from there"), Theme.Error);
            return;
        }

        var name = TextDrawing.ShortenHome(worktree.Path);
        using (var confirm = new ConfirmDialog(T($"Удалить worktree {name}?", $"Delete worktree {name}?"), CommandText.Format(GitService.RemoveWorktreeArguments(worktree.Path, false)),
                   T("Папка удалится. Ветка и её коммиты останутся в репозитории.", "The folder is deleted. The branch and its commits stay in the repository."), T("Удалить", "Delete")))
        {
            App!.Run(confirm);
            if (confirm.Result != true)
                return;
        }

        var result = await _git.RemoveWorktreeAsync(worktree.Path, force: false);
        if (result.Success)
        {
            Changed = true;
            App?.Invoke(() => ShowStatus($"{Theme.SymbolSuccess} {T($"Worktree {name} удалён", $"Worktree {name} deleted")}", Theme.Success));
            await LoadAsync();
            return;
        }

        App?.Invoke(() =>
        {
            using var force = new ConfirmDialog(T($"В {name} есть незакоммиченные изменения. Удалить всё равно?", $"{name} has uncommitted changes. Delete anyway?"),
                CommandText.Format(GitService.RemoveWorktreeArguments(worktree.Path, true)),
                T($"{Theme.SymbolWarning} Незакоммиченные изменения в этой папке пропадут насовсем.", $"{Theme.SymbolWarning} Uncommitted changes in this folder are lost for good."),
                T("Удалить всё равно", "Delete anyway"));
            App!.Run(force);
            if (force.Result == true)
                _ = RunAsync(ct => _git.RemoveWorktreeAsync(worktree.Path, force: true, ct), T($"Worktree {name} удалён", $"Worktree {name} deleted"));
        });
    }

    private async Task RunAsync(Func<CancellationToken, Task<GitResult>> action, string success)
    {
        if (_busy)
            return;

        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Выполняется…", "Running…")}", Theme.Muted);
        try
        {
            var result = await action(CancellationToken.None);
            if (result.Success)
                Changed = true;
            App?.Invoke(() => ShowStatus(result.Success ? $"{Theme.SymbolSuccess} {success}" : $"{Theme.SymbolError} {GitErrors.Explain(result)}",
                result.Success ? Theme.Success : Theme.Error));
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
