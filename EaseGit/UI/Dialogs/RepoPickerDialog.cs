using EaseGit.GitHub;
using EaseGit.Models;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Выбор репозитория GitHub: поиск по мере ввода, сверху недавние локальные, ниже свои с GitHub; можно ввести
/// любой «владелец/репо». ↑↓ — выбор, enter — открыть, esc — отмена. Result — репозиторий или null.
/// </summary>
public sealed class RepoPickerDialog : Runnable<GitHubRepo?>
{
    private readonly ClipboardTextField _query;
    private readonly RepoChoicesPanel _list;
    private readonly Label _status;
    private readonly Func<CancellationToken, Task<IReadOnlyList<RepoChoice>>> _load;
    private readonly CancellationTokenSource _loading = new();
    private IReadOnlyList<RepoChoice> _all = [];

    /// <param name="load">Загружает список: недавние локальные и свои с GitHub (это идёт в сеть).</param>
    public RepoPickerDialog(Func<CancellationToken, Task<IReadOnlyList<RepoChoice>>> load)
    {
        _load = load;
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = Dim.Percent(70);
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1),
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var prompt = new Label { Text = "> ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));
        _query = new ClipboardTextField { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill() };
        _query.SetScheme(Theme.Input);
        _query.TextChanged += (_, _) => ApplyFilter();
        _query.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };

        _list = new RepoChoicesPanel { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(1), BorderStyle = LineStyle.None, CanFocus = false };
        _status = new Label { X = 0, Y = Pos.AnchorEnd(), Width = Dim.Fill() };
        _status.SetScheme(Theme.Text(Theme.Muted));

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints(
        [
            new Hint("↑↓", T("выбор", "choose")),
            new Hint("enter", T("открыть", "open")),
            new Hint(T("владелец/репо", "owner/repo"), T("любой репозиторий", "any repository")),
            new Hint("esc", T("отмена", "cancel")),
        ]);

        box.Add(prompt, _query, _list, _status);
        Add(box, BoxTitle.For(box, T("Репозиторий", "Repository"), Theme.Accent), hints);
        _query.SetFocus();
    }

    protected override void OnIsRunningChanged(bool newIsRunning)
    {
        base.OnIsRunningChanged(newIsRunning);
        if (newIsRunning)
            _ = LoadAsync();
        else
            _loading.Cancel();
    }

    private async Task LoadAsync()
    {
        _status.Text = $"{Theme.SpinnerFrames[0]} {T("Загрузка репозиториев…", "Loading repositories…")}";
        try
        {
            var all = await _load(_loading.Token);
            App?.Invoke(() =>
            {
                _all = all;
                _status.Text = "";
                ApplyFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // Окно закрыто.
        }
        catch (Exception ex) when (ex is Git.GitCommandException or System.Text.Json.JsonException)
        {
            App?.Invoke(() => _status.Text = T($"{Theme.SymbolError} Список с GitHub не загрузился: можно ввести владелец/репо",
                $"{Theme.SymbolError} The list from GitHub didn't load: you can type owner/repo"));
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        // Стрелки и страницы листают список, пока фокус в поле поиска.
        if (key == Key.CursorDown || key == Key.CursorUp || key == Key.PageDown || key == Key.PageUp)
            return _list.NewKeyDownEvent(key);

        return base.OnKeyDown(key);
    }

    private void ApplyFilter()
    {
        var shown = RepoChoices.Filter(_all, _query.Text);
        _list.Empty = _query.Text.Trim().Length == 0 ? "" : T("Ничего не найдено. Введи владелец/репо", "Nothing found. Type owner/repo");
        _list.SetItems(shown);
    }

    private void Submit()
    {
        if (_list.Selected is not { } choice)
            return;

        Result = choice.Repo;
        RequestStop();
    }
}
