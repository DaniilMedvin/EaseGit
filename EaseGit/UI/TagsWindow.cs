using EaseGit.Git;
using EaseGit.Models;
using EaseGit.UI.Dialogs;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI;

/// <summary>
/// Теги: сверху список (свежие первыми), снизу коммит выбранного тега. n — новый тег на HEAD (с сообщением —
/// аннотированный), P — отправить на сервер, D — удалить (локально или ещё и на сервере), y — имя, esc — закрыть.
/// </summary>
public sealed class TagsWindow : Runnable
{
    private readonly GitService _git;
    private readonly TagsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private CancellationTokenSource? _detailsLoading;
    private bool _busy;

    public TagsWindow(GitService git)
    {
        _git = git;
        Width = Dim.Fill();
        Height = Dim.Fill();
        SetScheme(Theme.Base);

        _list = new TagsPanel { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Percent(40) };
        _list.SelectionChanged += (_, _) => _ = LoadDetailsAsync();
        _details = new DiffPanel { X = 0, Y = Pos.Bottom(_list), Width = Dim.Fill(), Height = Dim.Fill(2) };
        _detailsTitle = BoxTitle.For(_details, T("Коммит", "Commit"), Theme.Muted);

        _status = new Label { X = 1, Y = Pos.AnchorEnd(2), Width = Dim.Fill(1) };
        var hints = new HintBar { X = 1, Y = Pos.AnchorEnd() };
        hints.SetHints(
        [
            new Hint("n", T("новый на HEAD", "new on HEAD")),
            new Hint("P", T("отправить", "push")),
            new Hint("D", T("удалить", "delete")),
            new Hint("y", T("имя", "name"), Optional: true),
            new Hint("ctrl+d/u", "diff", Optional: true),
            new Hint("esc", T("закрыть", "close")),
        ]);

        Add(_list, BoxTitle.ForFocusable(_list, T("Теги", "Tags")), _details, _detailsTitle, _status, hints);
        _list.SetFocus();
    }

    /// <summary>Что-то изменилось (тег создан или удалён): главному окну стоит обновиться.</summary>
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

        if (key == Key.N)
            return Handled(CreateTag());
        if (key == Key.P.WithShift)
            return Handled(PushAsync());
        if (key == Key.D.WithShift)
            return Handled(DeleteAsync());
        if (key == Key.Y && _list.Selected is { } tag)
        {
            SystemClipboard.SetText(tag.Name);
            ShowStatus(T($"{Theme.SymbolSuccess} Имя {tag.Name} скопировано", $"{Theme.SymbolSuccess} Name {tag.Name} copied"), Theme.Success);
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
        try
        {
            var tags = await _git.GetTagsAsync();
            App?.Invoke(() =>
            {
                _list.SetItems(tags);
                if (!_busy)
                    ShowStatus(Plural(tags.Count, "тег", "тега", "тегов", "tag", "tags"), Theme.Muted);
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
        if (_list.Selected is not { } tag)
        {
            _details.SetMessage("");
            return;
        }

        var loading = new CancellationTokenSource();
        _detailsLoading = loading;
        _detailsTitle.Text = $" {Theme.SymbolTag}{tag.Name} {Theme.SymbolSeparator} {tag.ShortHash} ";
        try
        {
            var commit = await _git.FindCommitAsync(tag.Hash, loading.Token);
            if (commit is null)
                return;

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
            // Выбран другой тег.
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => _details.SetMessage($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}"));
        }
    }

    /// <summary>Тег на HEAD: имя, затем необязательное сообщение (с ним тег аннотированный — так принято для релизов).</summary>
    private Task CreateTag()
    {
        if (_busy)
            return Task.CompletedTask;

        var existing = _list.Items.Select(t => t.Name).ToHashSet();
        using var nameDialog = new InputDialog(T("Новый тег", "New tag"), T("На HEAD. Например v1.2.0", "On HEAD. E.g. v1.2.0"),
            T("дальше", "next"), name => name.Length == 0 ? T("Введи имя тега", "Enter a tag name")
                : name.Contains(' ') ? T("Пробелы нельзя", "No spaces")
                : existing.Contains(name) ? T($"Тег {name} уже есть", $"Tag {name} already exists") : null);
        App!.Run(nameDialog);
        if (nameDialog.Result is not { } name)
            return Task.CompletedTask;

        using var messageDialog = new InputDialog(T($"Тег {name}", $"Tag {name}"),
            T("Сообщение — по желанию: с ним тег аннотированный (для релизов)", "Message is optional: with it the tag is annotated (for releases)"),
            T("создать", "create"));
        App!.Run(messageDialog);
        if (messageDialog.Result is not { } message)
            return Task.CompletedTask;

        return RunAsync(ct => _git.CreateTagOnHeadAsync(name, message, ct),
            T($"Тег {name} поставлен на HEAD. На сервер: P", $"Tag {name} set on HEAD. To the server: P"));
    }

    private async Task PushAsync()
    {
        if (_busy || _list.Selected is not { } tag)
            return;

        var remote = await FirstRemoteAsync();
        if (remote is null)
            return;

        await RunAsync(ct => _git.PushTagAsync(remote, tag, ct), T($"Тег {tag.Name} отправлен в {remote}", $"Tag {tag.Name} pushed to {remote}"));
    }

    /// <summary>Удаление: только локально или ещё и на сервере — выбор, затем подтверждение с командой.</summary>
    private async Task DeleteAsync()
    {
        if (_busy || _list.Selected is not { } tag)
            return;

        var remotes = await _git.GetRemotesAsync();
        var remote = remotes.FirstOrDefault();
        App?.Invoke(() =>
        {
            string? fromRemote = null;
            if (remote is not null)
            {
                using var choice = new ChoiceDialog(T("Удалить тег", "Delete tag"), T($"Где удалить {Theme.SymbolTag}{tag.Name}?", $"Where to delete {Theme.SymbolTag}{tag.Name}?"),
                [
                    new Choice(T("Только здесь", "Only here"), T("Локальный тег; на сервере, если он там есть, останется", "The local tag; on the server, if it's there, it stays")),
                    new Choice(T($"Здесь и в {remote}", $"Here and in {remote}"), T("Релиз на GitHub по этому тегу останется без тега", "A GitHub release for this tag is left without it")),
                ]);
                App!.Run(choice);
                if (choice.Result is not { } index)
                    return;
                fromRemote = index == 1 ? remote : null;
            }

            var command = CommandText.Format(GitService.DeleteTagArguments(tag));
            if (fromRemote is not null)
                command = CommandText.Format(GitService.DeleteRemoteTagArguments(fromRemote, tag)) + " && " + command;
            using var confirm = new ConfirmDialog(T($"Удалить тег {tag.Name}?", $"Delete tag {tag.Name}?"), command,
                T("Коммит останется, исчезнет только метка. Вернуть: поставить тег заново на тот же коммит.",
                    "The commit stays, only the label goes away. To restore: tag the same commit again."), T("Удалить", "Delete"));
            App!.Run(confirm);
            if (confirm.Result != true)
                return;

            _ = RunAsync(ct => _git.DeleteTagAsync(tag, fromRemote, ct),
                fromRemote is null ? T($"Тег {tag.Name} удалён", $"Tag {tag.Name} deleted") : T($"Тег {tag.Name} удалён здесь и в {fromRemote}", $"Tag {tag.Name} deleted here and in {fromRemote}"));
        });
    }

    private async Task<string?> FirstRemoteAsync()
    {
        var remotes = await _git.GetRemotesAsync();
        if (remotes.Count == 0)
            App?.Invoke(() => ShowStatus(T($"{Theme.SymbolError} Remote не настроен. Добавь его: git remote add origin <адрес>",
                $"{Theme.SymbolError} No remote configured. Add one: git remote add origin <url>"), Theme.Error));
        return remotes.FirstOrDefault();
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
