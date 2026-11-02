using EaseGit.Git;
using EaseGit.Models;
using EaseGit.UI.Panels;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Выбор открытого issue для ссылки из коммита: список грузится при открытии, ↑↓ — выбор, enter — связать,
/// esc — закрыть. Result — выбранный issue или null.
/// </summary>
public sealed class IssuePickerDialog : Runnable<Issue?>
{
    private const int ListHeight = 12;

    private readonly IssuesPanel _list;

    public IssuePickerDialog(GitHubRepo repo, Func<CancellationToken, Task<IReadOnlyList<Issue>>> load)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        Height = ListHeight + 3;
        SetScheme(Theme.Base);

        // Рамка — у самого списка.
        _list = new IssuesPanel
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = ListHeight + 2,
            Empty = $"{Theme.SpinnerFrames[0]} {T("Загрузка issues…", "Loading issues…")}",
        };
        _list.SetScheme(Theme.Text(Theme.Accent));

        var hints = new HintBar { X = 1, Y = Pos.Bottom(_list) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("связать", "link")), new Hint("esc", T("закрыть", "close"))]);

        Add(_list, BoxTitle.For(_list, T($"Связать с issue · {repo}", $"Link an issue · {repo}"), Theme.Accent), hints);
        _list.SetFocus();
        _ = LoadAsync(load);
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Enter && _list.Selected is { } issue)
        {
            Result = issue;
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private async Task LoadAsync(Func<CancellationToken, Task<IReadOnlyList<Issue>>> load)
    {
        try
        {
            var issues = await load(CancellationToken.None);
            App?.Invoke(() =>
            {
                _list.Empty = T("Открытых issues нет", "No open issues");
                _list.SetItems(issues);
            });
        }
        catch (GitCommandException ex)
        {
            App?.Invoke(() =>
            {
                _list.Empty = $"{Theme.SymbolError} {GitErrors.Explain(ex.Result)}";
                _list.SetNeedsDraw();
            });
        }
    }
}
