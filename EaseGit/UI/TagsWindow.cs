using EaseGit.Git;
using EaseGit.GitHub;
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
/// С GitHub: R — релиз из тега (заметки из коммитов с прошлого тега), o — релиз в браузере; у тегов — метка релиза.
/// </summary>
public sealed class TagsWindow : Runnable
{
    private readonly GitService _git;
    private readonly GitHubService? _github;
    private readonly GitHubRepo? _repo;
    private readonly TagsPanel _list;
    private readonly DiffPanel _details;
    private readonly Label _detailsTitle;
    private readonly Label _status;
    private CancellationTokenSource? _detailsLoading;
    private bool _busy;

    /// <param name="github">GitHub для релизов или null (нет gh, не вошёл, remote не на GitHub).</param>
    public TagsWindow(GitService git, GitHubService? github = null, GitHubRepo? repo = null)
    {
        _git = git;
        _github = repo is null ? null : github;
        _repo = repo;
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
            .. _github is null ? (Hint[])[] : [new Hint("R", T("релиз", "release")), new Hint("o", T("релиз в браузере", "release in browser"), Optional: true)],
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
        {
            _ = LoadAsync();
            _ = LoadReleasesAsync();
        }
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
        if (key == Key.R.WithShift)
            return Handled(CreateReleaseAsync());
        if (key == Key.O)
            return Handled(OpenReleaseAsync());
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

    /// <summary>Релизы с GitHub — для меток у тегов; без сети меток просто нет.</summary>
    private async Task LoadReleasesAsync()
    {
        if (_github is null || _repo is null)
            return;

        try
        {
            var releases = await _github.ListReleasesAsync(_repo);
            App?.Invoke(() => _list.Releases = releases.GroupBy(r => r.TagName).ToDictionary(g => g.Key, g => g.First()));
        }
        catch (GitCommandException)
        {
            // Нет сети или прав: теги видны и без меток.
        }
    }

    /// <summary>
    /// R: релиз на GitHub из выбранного тега. Заголовок — имя тега, заметки — из коммитов с прошлого тега (можно править;
    /// пустые — GitHub составит сам). Тега нет на сервере — сначала он отправляется. Перед созданием — подтверждение с командой.
    /// </summary>
    private async Task CreateReleaseAsync()
    {
        if (_busy || _list.Selected is not { } tag)
            return;

        if (_github is null || _repo is not { } repo)
        {
            ShowStatus(T($"{Theme.SymbolError} Релизы — на GitHub: нужен gh, вход (ctrl+g в главном окне) и remote на GitHub",
                $"{Theme.SymbolError} Releases live on GitHub: gh, signing in (ctrl+g in the main window) and a GitHub remote are needed"), Theme.Error);
            return;
        }

        if (_list.Releases.ContainsKey(tag.Name))
        {
            ShowStatus(T($"{Theme.SymbolError} У тега {tag.Name} уже есть релиз: o — открыть в браузере", $"{Theme.SymbolError} Tag {tag.Name} already has a release: o opens it"), Theme.Error);
            return;
        }

        var remote = await FirstRemoteAsync();
        if (remote is null)
            return;

        _busy = true;
        ShowStatus($"{Theme.SpinnerFrames[0]} {T("Собираю заметки из коммитов…", "Collecting notes from commits…")}", Theme.Muted);
        bool pushed;
        string? previous;
        IReadOnlyList<Commit> commits;
        try
        {
            var pushedTask = _git.IsTagOnRemoteAsync(remote, tag);
            previous = await _git.GetPreviousTagAsync(tag);
            commits = await _git.GetReleaseCommitsAsync(tag, previous);
            pushed = await pushedTask;
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() => ShowStatus($"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}", Theme.Error));
            return;
        }
        finally
        {
            _busy = false;
        }

        App?.Invoke(() =>
        {
            ShowStatus("", Theme.Muted);
            var prerelease = ReleaseNotes.IsPrerelease(tag.Name);
            var since = previous is null ? T("с начала истории", "since the beginning") : T($"с {previous}", $"since {previous}");
            var info = $"{Theme.SymbolTag}{tag.Name} {Theme.SymbolSeparator} {Plural(commits.Count, "коммит", "коммита", "коммитов", "commit", "commits")} {since}"
                       + (prerelease ? $" {Theme.SymbolSeparator} {T("пре-релиз", "pre-release")}" : "");
            using var dialog = new PullRequestDialog(repo.NameWithOwner, info, tag.Name, ReleaseNotes.Build(commits, repo, tag.Name, previous),
                heading: T($"Новый релиз · {repo}", $"New release · {repo}"),
                tip: T("Пустые заметки — GitHub составит сам (--generate-notes)", "Empty notes: GitHub writes them itself (--generate-notes)"));
            App!.Run(dialog);
            if (dialog.Result is not { } release)
                return;

            var create = GitHubService.CreateReleaseArguments(repo, tag.Name, release.Title, release.Draft, prerelease, release.Body.Length == 0);
            var command = CommandText.Format(create, "gh");
            if (!pushed)
                command = CommandText.Format(GitService.PushTagArguments(remote, tag)) + " && " + command;
            var warning = (pushed ? "" : T($"Тега ещё нет в {remote}: сначала он отправится. ", $"The tag isn't in {remote} yet: it's pushed first. "))
                          + (release.Draft
                              ? T("Черновик видят только те, у кого есть доступ на запись.", "Only people with write access see a draft.")
                              : T("Релиз сразу увидят все, у кого есть доступ к репозиторию.", "Everyone with access to the repository sees the release right away."));
            using var confirm = new ConfirmDialog(T($"Создать релиз {tag.Name}?", $"Create release {tag.Name}?"), command, warning, T("Создать", "Create"));
            App!.Run(confirm);
            if (confirm.Result != true)
                return;

            _ = RunAsync(async ct =>
            {
                if (!pushed)
                {
                    var push = await _git.PushTagAsync(remote, tag, ct);
                    if (!push.Success)
                        return push;
                }

                var result = await _github.CreateReleaseAsync(repo, tag.Name, release.Title, release.Body, release.Draft, prerelease, ct);
                if (result.Success)
                    _ = LoadReleasesAsync();
                return result;
            }, T($"Релиз {tag.Name} создан: o — открыть в браузере", $"Release {tag.Name} created: o opens it in the browser"));
        });
    }

    /// <summary>o: релиз выбранного тега в браузере.</summary>
    private Task OpenReleaseAsync()
    {
        if (_github is null || _repo is null || _list.Selected is not { } tag)
            return Task.CompletedTask;

        if (!_list.Releases.ContainsKey(tag.Name))
        {
            ShowStatus(T($"У тега {tag.Name} нет релиза: R — создать", $"Tag {tag.Name} has no release: R creates one"), Theme.Muted);
            return Task.CompletedTask;
        }

        ShowStatus(T($"{Theme.SymbolSuccess} Открываю релиз {tag.Name}", $"{Theme.SymbolSuccess} Opening release {tag.Name}"), Theme.Success);
        return _github.OpenReleaseInBrowserAsync(_repo, tag.Name);
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
