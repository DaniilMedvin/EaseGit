using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Окно ввода сообщения коммита в стиле поля ввода Claude CLI: тип по tab, поле «>», предпросмотр
/// итогового заголовка со счётчиком символов и подсказки по Conventional Commits.
/// Result — итоговое сообщение или null (Esc).
/// </summary>
public sealed class CommitDialog : Runnable<string?>
{
    private const int DialogWidth = 72;

    private readonly ClipboardTextField _subject;
    private readonly TypeSelector _types;
    private readonly Label _preview;
    private readonly Label _counter;
    private readonly Label _tip;

    public CommitDialog(int stagedCount)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = 11;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 10,
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

        _preview = new Label { X = 0, Y = 4, Width = Dim.Fill(8) };
        _counter = new Label { X = Pos.AnchorEnd(), Y = 4 };
        _tip = new Label { X = 0, Y = 5, Width = Dim.Fill() };

        var staged = new Label { Text = $"Staged: {FilesWord(stagedCount)}", X = 0, Y = 7 };
        staged.SetScheme(Theme.Text(Theme.Muted));

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("enter", "коммит"), new Hint("tab", "тип"), new Hint("ctrl+c/x/v", "буфер"), new Hint("esc", "отмена")]);

        box.Add(_types, prompt, _subject, _preview, _counter, _tip, staged);
        Add(box, BoxTitle.For(box, "Коммит", Theme.Accent), hints);
        UpdatePreview();
        _subject.SetFocus();
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

        return base.OnKeyDown(key);
    }

    private void Submit()
    {
        var message = CommitMessage.Compose(_types.Selected, _subject.Text);
        if (message.Length == 0)
        {
            ShowTip(new CommitTip("Введи сообщение коммита", TipSeverity.Error));
            return;
        }

        Result = message;
        RequestStop();
    }

    private void UpdatePreview()
    {
        var message = CommitMessage.Compose(_types.Selected, _subject.Text);
        _preview.Text = message.Length == 0 ? "" : message;
        _preview.SetScheme(Theme.Text(Theme.Muted, TextStyle.Italic));

        _counter.Text = $"{message.Length}/{CommitMessage.MaxLength}";
        _counter.SetScheme(Theme.Text(message.Length switch
        {
            > CommitMessage.MaxLength => Theme.Error,
            > CommitMessage.RecommendedLength => Theme.Warning,
            _ => Theme.Dim,
        }));

        var tips = CommitMessage.Check(_types.Selected, _subject.Text);
        ShowTip(tips.Count > 0 ? tips[0] : null);
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

    private static string FilesWord(int count)
    {
        var mod10 = count % 10;
        var mod100 = count % 100;
        var word = mod10 == 1 && mod100 != 11 ? "файл"
            : mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? "файла"
            : "файлов";
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
