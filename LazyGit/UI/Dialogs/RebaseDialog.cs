using LazyGit.Git;
using LazyGit.Models;
using LazyGit.UI.Panels;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// План interactive rebase: коммиты от выбранного до HEAD, новые сверху.
/// p / r / e / s / f / d — действие (повторное нажатие возвращает pick), J / K или shift+↓↑ — переставить,
/// enter — выполнить, esc — отмена. Result — шаги в порядке списка или null.
/// </summary>
public sealed class RebaseDialog : Runnable<IReadOnlyList<RebaseStep>?>
{
    private readonly IReadOnlyList<Commit> _original;
    private readonly RebaseStepsPanel _list;
    private readonly Label _tip;
    private List<RebaseStep> _steps;

    /// <param name="commits">Коммиты диапазона, новые первыми.</param>
    public RebaseDialog(IReadOnlyList<Commit> commits)
    {
        _original = commits;
        _steps = commits.Select(c => new RebaseStep(c, RebaseAction.Pick)).ToList();
        X = Pos.Center();
        Y = Pos.Center();
        Width = Dim.Percent(80);
        // Рамка, пояснение, пустая строка и итог плана + строка подсказок.
        Height = Math.Min(commits.Count, 20) + 6;
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

        var info = new Label
        {
            Text = T($"{PluralRu(commits.Count, "коммит", "коммита", "коммитов")}, новые сверху. squash / fixup вливают коммит в тот, что ниже", $"{PluralEn(commits.Count, "commit", "commits")}, newest on top. squash / fixup merge a commit into the one below"),
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
        };
        info.SetScheme(Theme.Text(Theme.Muted));

        _list = new RebaseStepsPanel { X = 0, Y = 1, Width = Dim.Fill(), Height = Dim.Fill(1), BorderStyle = LineStyle.None };
        _list.SetItems(_steps);

        _tip = new Label { X = 0, Y = Pos.AnchorEnd(), Width = Dim.Fill() };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints(
        [
            new Hint("p", "pick", Optional: true),
            new Hint("r", "reword"),
            new Hint("e", "edit"),
            new Hint("s", "squash"),
            new Hint("f", "fixup"),
            new Hint("d", "drop"),
            new Hint("J/K", T("порядок", "order")),
            new Hint("enter", T("выполнить", "run")),
            new Hint("esc", T("отмена", "cancel")),
        ]);

        box.Add(info, _list, _tip);
        Add(box, BoxTitle.For(box, "Interactive rebase", Theme.Accent), hints);
        _list.SetFocus();
        UpdateTip();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc)
        {
            RequestStop();
            return true;
        }

        if (key == Key.Enter)
        {
            Submit();
            return true;
        }

        if (key == Key.J.WithShift || key == Key.CursorDown.WithShift)
            return Move(+1);
        if (key == Key.K.WithShift || key == Key.CursorUp.WithShift)
            return Move(-1);

        RebaseAction? action = key == Key.P ? RebaseAction.Pick
            : key == Key.R ? RebaseAction.Reword
            : key == Key.E ? RebaseAction.Edit
            : key == Key.S ? RebaseAction.Squash
            : key == Key.F ? RebaseAction.Fixup
            : key == Key.D ? RebaseAction.Drop
            : null;
        if (action is { } chosen)
        {
            SetAction(chosen);
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>Ставит действие выбранному коммиту; то же действие ещё раз — обратно pick. Reword спрашивает заголовок.</summary>
    private void SetAction(RebaseAction action)
    {
        var index = _list.SelectedIndex;
        if (index < 0)
            return;

        var step = _steps[index];
        if (step.Action == action && action != RebaseAction.Reword)
            action = RebaseAction.Pick;

        string? subject = null;
        if (action == RebaseAction.Reword)
        {
            using var input = new InputDialog("Reword", T($"{step.Commit.ShortHash} {Theme.SymbolSeparator} тело сообщения сохранится", $"{step.Commit.ShortHash} {Theme.SymbolSeparator} the message body is kept"),
                T("сохранить", "save"), text => text.Length == 0 ? T("Заголовок не может быть пустым", "The subject can't be empty") : null, initial: step.Subject);
            App!.Run(input);
            if (input.Result is not { } entered)
                return;
            subject = entered;
        }

        Update(index, step with { Action = action, NewSubject = subject });
    }

    private bool Move(int delta)
    {
        var index = _list.SelectedIndex;
        var target = index + delta;
        if (index < 0 || target < 0 || target >= _steps.Count)
            return true;

        var steps = _steps.ToList();
        (steps[index], steps[target]) = (steps[target], steps[index]);
        _steps = steps;
        _list.SetItems(_steps);
        UpdateTip();
        return true;
    }

    private void Update(int index, RebaseStep step)
    {
        var steps = _steps.ToList();
        steps[index] = step;
        _steps = steps;
        _list.SetItems(_steps);
        _list.SetNeedsDraw();
        UpdateTip();
    }

    private void Submit()
    {
        if (!RebaseTodo.HasChanges(_steps, _original))
        {
            ShowTip(T($"{Theme.SymbolWarning} Ничего не изменено: выбери действие (r, s, f, d…) или переставь коммиты", $"{Theme.SymbolWarning} Nothing changed: choose an action (r, s, f, d…) or reorder commits"), Theme.Warning);
            return;
        }

        if (RebaseTodo.Validate(_steps) is { } error)
        {
            ShowTip($"{Theme.SymbolWarning} {error}", Theme.Warning);
            return;
        }

        Result = _steps;
        RequestStop();
    }

    /// <summary>Внизу — итог плана или ошибка, если его нельзя выполнить.</summary>
    private void UpdateTip()
    {
        if (!RebaseTodo.HasChanges(_steps, _original))
            ShowTip("", Theme.Muted);
        else if (RebaseTodo.Validate(_steps) is { } error)
            ShowTip($"{Theme.SymbolWarning} {error}", Theme.Warning);
        else
            ShowTip(T($"Будет: {RebaseTodo.Summary(_steps, _original)}", $"Will do: {RebaseTodo.Summary(_steps, _original)}"), Theme.Muted);
    }

    private void ShowTip(string text, Color color)
    {
        _tip.Text = text;
        _tip.SetScheme(Theme.Text(color));
    }
}
