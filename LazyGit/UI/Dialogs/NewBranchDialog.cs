using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Окно создания ветки: поле «>», от какой ветки создаётся и подсказка, если имя не подходит.
/// Result — имя новой ветки или null (Esc).
/// </summary>
public sealed class NewBranchDialog : Runnable<string?>
{
    private const int DialogWidth = 60;

    private readonly ClipboardTextField _name;
    private readonly Label _tip;
    private readonly IReadOnlyCollection<string> _existing;

    /// <param name="from">Откуда создаётся ветка, например «main · a1b2c3d».</param>
    /// <param name="existing">Имена существующих веток: такое имя занято.</param>
    public NewBranchDialog(string from, IReadOnlyCollection<string> existing)
    {
        _existing = existing;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = 8;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 7,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var prompt = new Label { Text = "> ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));

        _name = new ClipboardTextField { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill() };
        _name.SetScheme(Theme.Input);
        _name.TextChanged += (_, _) => UpdateTip(showEmpty: false);
        _name.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };

        var source = new Label { Text = $"От: {from}", X = 0, Y = 2, Width = Dim.Fill() };
        source.SetScheme(Theme.Text(Theme.Muted));

        _tip = new Label { X = 0, Y = 4, Width = Dim.Fill() };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("enter", "создать и переключиться"), new Hint("esc", "отмена")]);

        box.Add(prompt, _name, source, _tip);
        Add(box, BoxTitle.For(box, "Новая ветка", Theme.Accent), hints);
        _name.SetFocus();
    }

    private void Submit()
    {
        if (!UpdateTip(showEmpty: true))
            return;

        Result = _name.Text;
        RequestStop();
    }

    /// <summary>Показывает ошибку в имени. Пустое имя — ошибка только при попытке создать. Возвращает true, если имя подходит.</summary>
    private bool UpdateTip(bool showEmpty)
    {
        var name = _name.Text;
        var error = name.Length == 0 && !showEmpty ? null : BranchName.Check(name, _existing);
        _tip.Text = error is null ? "" : $"{Theme.SymbolWarning} {error}";
        _tip.SetScheme(Theme.Text(Theme.Warning));
        return error is null;
    }
}
