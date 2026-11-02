using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Новый issue (заголовок «>» и описание) или комментарий (только текст). enter в заголовке / ctrl+s — отправить,
/// ↓ — в текст, esc — отмена. Result — введённое или null.
/// </summary>
public sealed class IssueDialog : Runnable<IssueText?>
{
    private const int BodyHeight = 10;

    private readonly ClipboardTextField? _title;
    private readonly ClipboardTextView _body;
    private readonly Label _tip;

    /// <param name="withTitle">Нужен заголовок (новый issue); без него — комментарий.</param>
    public IssueDialog(string heading, string info, bool withTitle)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = 76;
        Height = BodyHeight + (withTitle ? 9 : 7);
        SetScheme(Theme.Base);

        var box = new View { X = 0, Y = 0, Width = Dim.Fill(), Height = Dim.Fill(1), BorderStyle = LineStyle.Rounded, CanFocus = true };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var infoLabel = new Label { Text = info, X = 0, Y = 0, Width = Dim.Fill() };
        infoLabel.SetScheme(Theme.Text(Theme.Info));
        box.Add(infoLabel);

        var y = 2;
        if (withTitle)
        {
            var prompt = new Label { Text = "> ", X = 0, Y = y };
            prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));
            _title = new ClipboardTextField { X = Pos.Right(prompt), Y = y, Width = Dim.Fill(), BeforeKey = HandleWindowKey };
            _title.SetScheme(Theme.Input);
            _title.Accepting += (_, e) =>
            {
                e.Handled = true;
                Submit();
            };
            box.Add(prompt, _title);
            y += 2;
        }

        var label = new Label { Text = withTitle ? T("Описание (Markdown)", "Description (Markdown)") : T("Комментарий (Markdown)", "Comment (Markdown)"), X = 0, Y = y };
        label.SetScheme(Theme.Text(Theme.Muted));
        var bar = new Label { X = 0, Y = y + 1, Width = 2, Height = BodyHeight, Text = string.Join('\n', Enumerable.Repeat("│", BodyHeight)) };
        bar.SetScheme(Theme.Text(Theme.Dim));
        _body = new ClipboardTextView { X = 2, Y = y + 1, Width = Dim.Fill(), Height = BodyHeight, TabKeyAddsTab = false, WordWrap = true, BeforeKey = HandleWindowKey };
        _body.SetScheme(Theme.Input);
        _tip = new Label { X = 0, Y = Pos.AnchorEnd(), Width = Dim.Fill() };
        box.Add(label, bar, _body, _tip);

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints(
        [
            new Hint("ctrl+s", T("отправить", "send")),
            .. withTitle ? (Hint[])[new Hint("↓ ↑", T("заголовок / текст", "title / text"))] : [],
            new Hint("esc", T("отмена", "cancel")),
        ]);
        Add(box, BoxTitle.For(box, heading, Theme.Accent), hints);
        ((View?)_title ?? _body).SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (HandleWindowKey(key))
            return true;
        if (_title is not null && _title.HasFocus && key == Key.CursorDown)
        {
            _body.SetFocus();
            return true;
        }

        if (_title is not null && _body.HasFocus && key == Key.CursorUp && _body.CurrentRow == 0)
        {
            _title.SetFocus();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private bool HandleWindowKey(Key key)
    {
        if (key != Key.S.WithCtrl)
            return false;

        Submit();
        return true;
    }

    private void Submit()
    {
        var title = _title?.Text.Trim();
        var body = _body.Text.Trim();
        var error = title is { Length: 0 } ? T("Введи заголовок", "Enter a title")
            : _title is null && body.Length == 0 ? T("Комментарий пустой", "The comment is empty")
            : null;
        if (error is not null)
        {
            _tip.Text = $"{Theme.SymbolError} {error}";
            _tip.SetScheme(Theme.Text(Theme.Error));
            return;
        }

        Result = new IssueText(title, body);
        RequestStop();
    }
}
