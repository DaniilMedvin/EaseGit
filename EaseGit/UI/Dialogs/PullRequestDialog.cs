using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Dialogs;

/// <summary>Что ввели в окне нового PR.</summary>
public sealed record NewPullRequest(string Title, string Body, bool Draft);

/// <summary>
/// Новый PR (и релиз из тега): заголовок «>», описание, ветки «feature/x → main» и признак черновика.
/// enter / ctrl+s — создать, ↓ — в описание, ctrl+d — черновик, esc — отмена. Result — введённое или null.
/// </summary>
public sealed class PullRequestDialog : Runnable<NewPullRequest?>
{
    private const int DialogWidth = 76;
    private const int BodyHeight = 8;

    private readonly ClipboardTextField _title;
    private readonly ClipboardTextView _body;
    private readonly Label _draft;
    private readonly Label _tip;
    private readonly HintBar _hints;
    private bool _isDraft;

    /// <param name="info">«feature/x → main · 3 коммита».</param>
    /// <param name="heading">Заголовок окна; null — «Новый PR · репо».</param>
    /// <param name="tip">Подсказка под описанием (muted), пока нет ошибки.</param>
    public PullRequestDialog(string repo, string info, string title, string body, string? heading = null, string? tip = null)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = BodyHeight + 10;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = BodyHeight + 9,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var infoLabel = new Label { Text = info, X = 0, Y = 0, Width = Dim.Fill(12) };
        infoLabel.SetScheme(Theme.Text(Theme.Info));
        _draft = new Label { X = Pos.AnchorEnd(), Y = 0 };

        var prompt = new Label { Text = "> ", X = 0, Y = 2 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));
        _title = new ClipboardTextField { X = Pos.Right(prompt), Y = 2, Width = Dim.Fill(), Text = title, BeforeKey = HandleWindowKey };
        _title.SetScheme(Theme.Input);
        _title.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };
        _title.HasFocusChanged += (_, _) => UpdateHints();

        var bodyLabel = new Label { Text = T("Описание (Markdown)", "Description (Markdown)"), X = 0, Y = 4 };
        bodyLabel.SetScheme(Theme.Text(Theme.Muted));
        var bar = new Label { X = 0, Y = 5, Width = 2, Height = BodyHeight, Text = string.Join('\n', Enumerable.Repeat("│", BodyHeight)) };
        bar.SetScheme(Theme.Text(Theme.Dim));
        _body = new ClipboardTextView
        {
            X = 2,
            Y = 5,
            Width = Dim.Fill(),
            Height = BodyHeight,
            TabKeyAddsTab = false,
            WordWrap = true,
            Text = body,
            // ctrl+d поле считает «удалить символ», ctrl+s — своим; здесь это черновик и «создать».
            BeforeKey = HandleWindowKey,
        };
        _body.SetScheme(Theme.Input);
        _body.HasFocusChanged += (_, _) => UpdateHints();

        _tip = new Label { X = 0, Y = BodyHeight + 6, Width = Dim.Fill(), Text = tip ?? "" };
        _tip.SetScheme(Theme.Text(Theme.Muted));

        _hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        box.Add(infoLabel, _draft, prompt, _title, bodyLabel, bar, _body, _tip);
        Add(box, BoxTitle.For(box, heading ?? T($"Новый PR · {repo}", $"New PR · {repo}"), Theme.Accent), _hints);
        UpdateDraft();
        _title.SetFocus();
        UpdateHints();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (HandleWindowKey(key))
            return true;

        if (_title.HasFocus && key == Key.CursorDown)
        {
            _body.SetFocus();
            return true;
        }

        if (_body.HasFocus && key == Key.CursorUp && _body.CurrentRow == 0)
        {
            _title.SetFocus();
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>ctrl+s — создать, ctrl+d — черновик; вызывается и из полей ввода, раньше их собственной обработки.</summary>
    private bool HandleWindowKey(Key key)
    {
        if (key == Key.S.WithCtrl)
        {
            Submit();
            return true;
        }

        if (key == Key.D.WithCtrl)
        {
            _isDraft = !_isDraft;
            UpdateDraft();
            return true;
        }

        return false;
    }

    private void Submit()
    {
        var title = _title.Text.Trim();
        if (title.Length == 0)
        {
            _tip.Text = $"{Theme.SymbolError} {T("Введи заголовок", "Enter the title")}";
            _tip.SetScheme(Theme.Text(Theme.Error));
            _title.SetFocus();
            return;
        }

        Result = new NewPullRequest(title, _body.Text.Trim(), _isDraft);
        RequestStop();
    }

    private void UpdateDraft()
    {
        _draft.Text = _isDraft ? T(" черновик ", " draft ") : "";
        _draft.SetScheme(Theme.Text(Theme.Warning, TextStyle.Bold));
    }

    private void UpdateHints() =>
        _hints.SetHints(
        [
            _body.HasFocus ? new Hint("ctrl+s", T("создать", "create")) : new Hint("enter", T("создать", "create")),
            _body.HasFocus ? new Hint("↑", T("заголовок", "title")) : new Hint("↓", T("описание", "description")),
            new Hint("ctrl+d", T("черновик", "draft")),
            new Hint("esc", T("отмена", "cancel")),
        ]);
}
