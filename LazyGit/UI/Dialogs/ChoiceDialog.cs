using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>Вариант в <see cref="ChoiceDialog"/>: название и пояснение под ним.</summary>
public sealed record Choice(string Title, string Description);

/// <summary>
/// Выбор одного варианта из нескольких: ↑↓ / j k, enter — выбрать, esc — отмена. Result — номер варианта или null.
/// </summary>
public sealed class ChoiceDialog : Runnable<int?>
{
    private const int DialogWidth = 64;

    private readonly IReadOnlyList<Choice> _choices;
    private readonly View _list;
    private int _selected;

    public ChoiceDialog(string title, string question, IReadOnlyList<Choice> choices)
    {
        _choices = choices;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = choices.Count * 2 + 6;
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

        var questionLabel = new Label { Text = question, X = 0, Y = 0, Width = Dim.Fill() };
        questionLabel.SetScheme(Theme.Text(Theme.Fg, TextStyle.Bold));

        _list = new View { X = 0, Y = 2, Width = Dim.Fill(), Height = choices.Count * 2 };
        _list.DrawingContent += (_, _) => DrawChoices();

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("выбрать", "select")), new Hint("esc", T("отмена", "cancel"))]);

        box.Add(questionLabel, _list);
        Add(box, BoxTitle.For(box, title, Theme.Accent), hints);
        box.SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.J)
            return Move(+1);
        if (key == Key.CursorUp || key == Key.K)
            return Move(-1);
        if (key == Key.Enter)
        {
            Result = _selected;
            RequestStop();
            return true;
        }

        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private bool Move(int step)
    {
        _selected = Math.Clamp(_selected + step, 0, _choices.Count - 1);
        _list.SetNeedsDraw();
        return true;
    }

    private void DrawChoices()
    {
        for (var i = 0; i < _choices.Count; i++)
        {
            var selected = i == _selected;
            _list.DrawSegments(0, i * 2,
            [
                new Segment(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold),
                new Segment(_choices[i].Title, selected ? Theme.Accent : Theme.Fg, selected ? TextStyle.Bold : TextStyle.None),
            ], _list.Viewport.Width);
            _list.DrawSegments(2, i * 2 + 1, [new Segment(_choices[i].Description, Theme.Muted)], _list.Viewport.Width - 2);
        }
    }
}
