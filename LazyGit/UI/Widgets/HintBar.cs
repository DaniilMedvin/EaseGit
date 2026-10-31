using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Widgets;

public sealed record Hint(string Key, string Description);

/// <summary>Строка подсказок по клавишам: «клавиша описание · клавиша описание».</summary>
public sealed class HintBar : View
{
    public HintBar()
    {
        Width = Dim.Fill();
        Height = 1;
        CanFocus = false;
    }

    public void SetHints(IEnumerable<Hint> hints)
    {
        RemoveAll();
        View? previous = null;
        foreach (var hint in hints)
        {
            if (previous is not null)
                previous = AddPart($" {Theme.SymbolSeparator} ", Theme.Muted, previous);
            previous = AddPart(hint.Key, Theme.Fg, previous);
            previous = AddPart(" " + hint.Description, Theme.Muted, previous);
        }
    }

    private Label AddPart(string text, Terminal.Gui.Drawing.Color color, View? after)
    {
        var label = new Label
        {
            Text = text,
            X = after is null ? 0 : Pos.Right(after),
            Y = 0,
        };
        label.SetScheme(Theme.Text(color));
        Add(label);
        return label;
    }
}
