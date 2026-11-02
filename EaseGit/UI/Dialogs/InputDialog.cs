using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Окно ввода одной строки: поле «>», строка пояснения и подсказка, если ввод не подходит.
/// Используется для новой ветки, сообщения stash и reword. Result — введённый текст или null (Esc).
/// </summary>
public sealed class InputDialog : Runnable<string?>
{
    private const int DialogWidth = 60;

    private readonly ClipboardTextField _input;
    private readonly Label _tip;
    private readonly Func<string, string?> _validate;

    /// <param name="title">Заголовок рамки: «Новая ветка».</param>
    /// <param name="info">Пояснение под полем: «От: main · a1b2c3d».</param>
    /// <param name="submit">Что делает enter: «создать и переключиться».</param>
    /// <param name="validate">Текст ошибки для введённого значения или null, если подходит. Пустой ввод
    /// проверяется только при enter, чтобы не ругаться на ещё не начатое поле.</param>
    /// <param name="initial">Начальный текст поля, например прежний заголовок коммита.</param>
    public InputDialog(string title, string info, string submit, Func<string, string?>? validate = null, string initial = "")
    {
        _validate = validate ?? (_ => null);
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

        _input = new ClipboardTextField { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill(), Text = initial };
        _input.SetScheme(Theme.Input);
        _input.TextChanged += (_, _) => UpdateTip(onSubmit: false);
        _input.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };

        var infoLabel = new Label { Text = info, X = 0, Y = 2, Width = Dim.Fill() };
        infoLabel.SetScheme(Theme.Text(Theme.Muted));

        _tip = new Label { X = 0, Y = 4, Width = Dim.Fill() };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("enter", submit), new Hint("esc", T("отмена", "cancel"))]);

        box.Add(prompt, _input, infoLabel, _tip);
        Add(box, BoxTitle.For(box, title, Theme.Accent), hints);
        _input.SetFocus();
    }

    private void Submit()
    {
        if (!UpdateTip(onSubmit: true))
            return;

        Result = _input.Text.Trim();
        RequestStop();
    }

    /// <summary>Показывает ошибку ввода. Возвращает true, если ввод подходит.</summary>
    private bool UpdateTip(bool onSubmit)
    {
        var text = _input.Text.Trim();
        var error = text.Length == 0 && !onSubmit ? null : _validate(text);
        _tip.Text = error is null ? "" : $"{Theme.SymbolWarning} {error}";
        _tip.SetScheme(Theme.Text(Theme.Warning));
        return error is null;
    }
}
