using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Подтверждение опасного действия: рамка цвета warning, вопрос, точная git-команда и предупреждение.
/// Фокус по умолчанию на «Отмена». ← → / tab — выбор кнопки, enter — нажать, esc — отмена.
/// Result — true, если действие подтверждено.
/// </summary>
public sealed class ConfirmDialog : Runnable<bool?>
{
    private const int DialogWidth = 62;

    private readonly Button _cancel;
    private readonly Button _confirm;

    /// <param name="question">Вопрос: «Удалить ветку feature/x?».</param>
    /// <param name="command">Команда, которая будет выполнена.</param>
    /// <param name="warning">Чем грозит действие, например «Это нельзя отменить.».</param>
    /// <param name="confirmText">Текст кнопки подтверждения: «Удалить».</param>
    public ConfirmDialog(string question, string command, string warning, string confirmText)
    {
        var textWidth = DialogWidth - 4;
        var commandLines = Wrap(command, textWidth - 2);
        var warningLines = Wrap(warning, textWidth);
        var height = 8 + commandLines.Count + warningLines.Count;

        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = height + 1;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = height,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Warning));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var questionLabel = new Label { Text = question, X = 0, Y = 0, Width = Dim.Fill() };
        questionLabel.SetScheme(Theme.Text(Theme.Fg, TextStyle.Bold));

        var caption = new Label { Text = T("Будет выполнено:", "Will run:"), X = 0, Y = 2 };
        caption.SetScheme(Theme.Text(Theme.Muted));

        var commandLabel = new Label
        {
            Text = string.Join('\n', commandLines),
            X = 2,
            Y = 3,
            Width = Dim.Fill(),
            Height = commandLines.Count,
        };
        commandLabel.SetScheme(Theme.Text(Theme.Info));

        var warningLabel = new Label
        {
            Text = string.Join('\n', warningLines),
            X = 0,
            Y = 4 + commandLines.Count,
            Width = Dim.Fill(),
            Height = warningLines.Count,
        };
        warningLabel.SetScheme(Theme.Text(Theme.Warning));

        _cancel = CreateButton(T("Отмена", "Cancel"), false);
        _confirm = CreateButton(confirmText, true);
        _cancel.X = Pos.Center() - (confirmText.Length + 4) / 2 - 6;
        _cancel.Y = 5 + commandLines.Count + warningLines.Count;
        _confirm.X = Pos.Right(_cancel) + 4;
        _confirm.Y = _cancel.Y;

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("← →", T("выбор", "choose")), new Hint("enter", T("нажать", "press")), new Hint("esc", T("отмена", "cancel"))]);

        box.Add(questionLabel, caption, commandLabel, warningLabel, _cancel, _confirm);
        Add(box, BoxTitle.For(box, T($"{Theme.SymbolWarning} Опасное действие", $"{Theme.SymbolWarning} Dangerous action"), Theme.Warning), hints);
        _cancel.SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorLeft || key == Key.CursorRight || key == Key.Tab || key == Key.Tab.WithShift
            || key == Key.H || key == Key.L)
        {
            (_cancel.HasFocus ? _confirm : _cancel).SetFocus();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private Button CreateButton(string text, bool confirms)
    {
        var button = new Button
        {
            Text = $"  {text}  ",
            NoDecorations = true,
            ShadowStyle = ShadowStyles.None,
        };
        button.SetScheme(Theme.Button);
        button.Accepting += (_, e) =>
        {
            e.Handled = true;
            Result = confirms;
            RequestStop();
        };
        return button;
    }

    /// <summary>Переносит текст по словам в строки не длиннее <paramref name="width"/>.</summary>
    private static List<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var current = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current.Length > 0 && current.Length + 1 + word.Length > width)
            {
                lines.Add(current);
                current = word;
            }
            else
            {
                current = current.Length == 0 ? word : current + " " + word;
            }
        }

        if (current.Length > 0)
            lines.Add(current);
        return lines;
    }
}
