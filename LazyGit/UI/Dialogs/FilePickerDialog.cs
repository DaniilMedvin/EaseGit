using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Выбор файла репозитория по части имени: ↑↓ — выбор, enter — открыть, esc — отмена. Result — путь или null.
/// Список файлов загружается в самом окне, чтобы его можно было открыть прямо из обработчика клавиши.
/// </summary>
public sealed class FilePickerDialog : Runnable<string?>
{
    private readonly Func<CancellationToken, Task<IReadOnlyList<string>>> _load;
    private readonly CancellationTokenSource _loading = new();
    private IReadOnlyList<string> _paths = [];
    private readonly ClipboardTextField _query;
    private readonly PathsPanel _list;
    private readonly Label _count;

    public FilePickerDialog(string title, Func<CancellationToken, Task<IReadOnlyList<string>>> load)
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

        // Список создаётся раньше поля: обработчик enter в поле обращается к нему.
        _list = new PathsPanel { X = 0, Y = 2, Width = Dim.Fill(), Height = Dim.Fill(), BorderStyle = LineStyle.None, CanFocus = false };

        var prompt = new Label { Text = "> ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));
        _query = new ClipboardTextField { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill(10) };
        _query.SetScheme(Theme.Input);
        _query.TextChanged += (_, _) => ApplyFilter();
        _query.Accepting += (_, e) =>
        {
            e.Handled = true;
            if (_list.Selected is { } path)
            {
                Result = path;
                RequestStop();
            }
        };
        _count = new Label { X = Pos.AnchorEnd(), Y = 0 };
        _count.SetScheme(Theme.Text(Theme.Dim));


        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("открыть", "open")), new Hint("esc", T("отмена", "cancel"))]);

        box.Add(prompt, _query, _count, _list);
        Add(box, BoxTitle.For(box, title, Theme.Accent), hints);
        ApplyFilter();
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
        _count.Text = Theme.SpinnerFrames[0].ToString();
        try
        {
            var paths = await _load(_loading.Token);
            App?.Invoke(() =>
            {
                _paths = paths;
                ApplyFilter();
            });
        }
        catch (OperationCanceledException)
        {
            // Окно закрыто.
        }
        catch (Git.GitCommandException ex)
        {
            App?.Invoke(() => _count.Text = $"{Theme.SymbolError} {GitErrors.FirstLine(ex.Result.StdErr)}");
        }
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.CursorDown || key == Key.CursorUp || key == Key.PageDown || key == Key.PageUp)
            return _list.NewKeyDownEvent(key);

        return base.OnKeyDown(key);
    }

    private void ApplyFilter()
    {
        var shown = PathSearch.Filter(_paths, _query.Text);
        _list.Empty = T("Нет таких файлов", "No such files");
        _list.SetItems(shown);
        _count.Text = $"{shown.Count}/{_paths.Count}";
    }
}
