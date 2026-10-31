using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Окно ввода сообщения коммита в стиле поля ввода Claude CLI: тип по tab, заголовок «>»,
/// необязательное тело, предпросмотр заголовка со счётчиком и подсказки по Conventional Commits.
/// Result — итоговое сообщение или null (Esc).
/// </summary>
public sealed class CommitDialog : Runnable<string?>
{
    private const int DialogWidth = 72;
    private const int BodyHeight = 6;

    private readonly ClipboardTextField _subject;
    private readonly ClipboardTextView _body;
    private readonly TypeSelector _types;
    private readonly Label _preview;
    private readonly Label _counter;
    private readonly Label _tip;
    private readonly Label _bodyLabel;
    private readonly HintBar _hints;

    public CommitDialog(int stagedCount)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = BodyHeight + 13;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = BodyHeight + 12,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        _types = new TypeSelector { X = 0, Y = 0 };
        _types.Changed += (_, _) => UpdatePreview();

        var prompt = new Label { Text = "> ", X = 0, Y = 2 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));

        _subject = new ClipboardTextField { X = Pos.Right(prompt), Y = 2, Width = Dim.Fill() };
        _subject.SetScheme(Theme.Input);
        _subject.TextChanged += (_, _) => UpdatePreview();
        _subject.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };
        _subject.HasFocusChanged += (_, _) => UpdateFocusHints();

        _preview = new Label { X = 0, Y = 4, Width = Dim.Fill(8) };
        _counter = new Label { X = Pos.AnchorEnd(), Y = 4 };
        _tip = new Label { X = 0, Y = 5, Width = Dim.Fill() };

        _bodyLabel = new Label { X = 0, Y = 7, Width = Dim.Fill() };

        var bar = new Label { X = 0, Y = 8, Width = 2, Height = BodyHeight, Text = string.Join('\n', Enumerable.Repeat("│", BodyHeight)) };
        bar.SetScheme(Theme.Text(Theme.Dim));

        _body = new ClipboardTextView
        {
            X = 2,
            Y = 8,
            Width = Dim.Fill(),
            Height = BodyHeight,
            TabKeyAddsTab = false,
            // Перенос только на экране: текст коммита не меняется, длинные строки отмечает подсказка.
            WordWrap = true,
        };
        _body.SetScheme(Theme.Input);
        _body.ContentsChanged += (_, _) => UpdatePreview();
        _body.HasFocusChanged += (_, _) => UpdateFocusHints();

        var staged = new Label { Text = $"Staged: {FilesWord(stagedCount)}", X = 0, Y = BodyHeight + 9 };
        staged.SetScheme(Theme.Text(Theme.Muted));

        _hints = new HintBar { X = 1, Y = Pos.Bottom(box) };

        box.Add(_types, prompt, _subject, _preview, _counter, _tip, _bodyLabel, bar, _body, staged);
        Add(box, BoxTitle.For(box, "Коммит", Theme.Accent), _hints);
        UpdatePreview();
        _subject.SetFocus();
        UpdateFocusHints();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Tab)
        {
            _types.Move(+1);
            return true;
        }

        if (key == Key.Tab.WithShift)
        {
            _types.Move(-1);
            return true;
        }

        if (key == Key.S.WithCtrl)
        {
            Submit();
            return true;
        }

        if (_subject.HasFocus && (key == Key.CursorDown || key == Key.J.WithCtrl))
        {
            _body.SetFocus();
            return true;
        }

        // ↑ на первой строке тела возвращает к заголовку.
        if (_body.HasFocus && key == Key.CursorUp && _body.CurrentRow == 0)
        {
            _subject.SetFocus();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private string Message => CommitMessage.Compose(_types.Selected, _subject.Text, _body.Text);

    private void Submit()
    {
        if (CommitMessage.Compose(_types.Selected, _subject.Text).Length == 0)
        {
            ShowTip(new CommitTip("Введи заголовок коммита", TipSeverity.Error));
            _subject.SetFocus();
            return;
        }

        Result = Message;
        RequestStop();
    }

    private void UpdatePreview()
    {
        var title = CommitMessage.Compose(_types.Selected, _subject.Text);
        _preview.Text = title;
        _preview.SetScheme(Theme.Text(Theme.Muted, TextStyle.Italic));

        _counter.Text = $"{title.Length}/{CommitMessage.MaxLength}";
        _counter.SetScheme(Theme.Text(title.Length switch
        {
            > CommitMessage.MaxLength => Theme.Error,
            > CommitMessage.RecommendedLength => Theme.Warning,
            _ => Theme.Dim,
        }));

        var message = Message;
        var bodyStart = message.IndexOf("\n\n", StringComparison.Ordinal);
        var bodyLines = bodyStart < 0 ? 0 : message[(bodyStart + 2)..].Split('\n').Length;
        _bodyLabel.Text = bodyLines > 0
            ? $"Тело · {LinesWord(bodyLines)}"
            : "Тело — что и зачем, например «- add …» (необязательно)";
        _bodyLabel.SetScheme(Theme.Text(Theme.Muted));

        var tips = CommitMessage.Check(_types.Selected, _subject.Text).Concat(CommitMessage.CheckBody(_body.Text)).ToList();
        ShowTip(tips.Count > 0 ? tips[0] : null);
    }

    private void UpdateFocusHints()
    {
        _hints.SetHints(_body.HasFocus
            ?
            [
                new Hint("ctrl+s", "коммит"),
                new Hint("enter", "строка"),
                new Hint("↑", "заголовок"),
                new Hint("tab", "тип"),
                new Hint("esc", "отмена"),
            ]
            :
            [
                new Hint("enter", "коммит"),
                new Hint("↓", "тело"),
                new Hint("tab", "тип"),
                new Hint("ctrl+c/x/v", "буфер"),
                new Hint("esc", "отмена"),
            ]);
    }

    private void ShowTip(CommitTip? tip)
    {
        if (tip is null)
        {
            _tip.Text = "";
            return;
        }

        var (symbol, color) = tip.Severity switch
        {
            TipSeverity.Error => (Theme.SymbolError, Theme.Error),
            TipSeverity.Warning => (Theme.SymbolWarning, Theme.Warning),
            _ => ("i", Theme.Info),
        };
        _tip.Text = $"{symbol} {tip.Text}";
        _tip.SetScheme(Theme.Text(color));
    }

    private static string FilesWord(int count) => Plural(count, "файл", "файла", "файлов");

    private static string LinesWord(int count) => Plural(count, "строка", "строки", "строк");

    private static string Plural(int count, string one, string few, string many)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = mod10 == 1 && mod100 != 11 ? one
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? few
            : many;
        return $"{count} {word}";
    }

    /// <summary>Строка выбора типа коммита: выбранный тип выделен фоном акцентного цвета.</summary>
    private sealed class TypeSelector : View
    {
        private int _index;

        public TypeSelector()
        {
            Width = Dim.Fill();
            Height = 1;
            CanFocus = false;
        }

        public event EventHandler? Changed;

        public string? Selected => CommitMessage.Types[_index];

        public void Move(int delta)
        {
            var count = CommitMessage.Types.Length;
            _index = ((_index + delta) % count + count) % count;
            SetNeedsDraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var x = 0;
            for (var i = 0; i < CommitMessage.Types.Length; i++)
            {
                var name = CommitMessage.Types[i] ?? "без типа";
                var selected = i == _index;
                x += this.DrawSegments(x, 0,
                    [new Segment($" {name} ", selected ? Theme.Backdrop : Theme.Muted, selected ? TextStyle.Bold : TextStyle.None)],
                    Viewport.Width - x, selected ? Theme.Accent : null);
                x += this.DrawSegments(x, 0, [new Segment(" ", Theme.Muted)], Viewport.Width - x);
            }

            return true;
        }
    }
}
