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

    private readonly Func<CancellationToken, Task<CommitSuggestion?>>? _suggest;
    private readonly IssueSource? _issues;

    /// <param name="suggest">«По коду» (ctrl+e): предложение сообщения по изменённым классам и методам; null — недоступно.</param>
    /// <param name="issues">Issues репозитория для ссылки (ctrl+k); null — репозиторий не на GitHub или gh не готов.</param>
    public CommitDialog(int stagedCount, Func<CancellationToken, Task<CommitSuggestion?>>? suggest = null, IssueSource? issues = null)
    {
        _suggest = suggest;
        _issues = issues;
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

        // ctrl+e поле считает «в конец строки» — здесь это «по коду», поэтому окно получает её первым.
        _subject = new ClipboardTextField { X = Pos.Right(prompt), Y = 2, Width = Dim.Fill(), BeforeKey = HandleWindowKey };
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
            BeforeKey = HandleWindowKey,
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
        Add(box, BoxTitle.For(box, T("Коммит", "Commit"), Theme.Accent), _hints);
        UpdatePreview();
        _subject.SetFocus();
        UpdateFocusHints();
    }

    /// <summary>ctrl+e — «по коду», ctrl+k — ссылка на issue; вызывается и из полей ввода, раньше их собственной обработки.</summary>
    private bool HandleWindowKey(Key key)
    {
        if (key == Key.K.WithCtrl && _issues is not null)
        {
            LinkIssue();
            return true;
        }

        if (key != Key.E.WithCtrl || _suggest is null)
            return false;

        _ = SuggestAsync();
        return true;
    }

    /// <summary>Выбор issue и «Fixes #12» / «Closes #12» в конец тела: issue закроется, когда коммит попадёт в main.</summary>
    private void LinkIssue()
    {
        using var picker = new IssuePickerDialog(_issues!.Repo, _issues.Load);
        App?.Run(picker);
        if (picker.Result is not { } issue)
            return;

        _body.Text = CommitMessage.LinkIssue(_body.Text, _types.Selected, issue.Number);
        UpdatePreview();
        ShowTip(new CommitTip(T($"Issue #{issue.Number} закроется, когда коммит попадёт в основную ветку",
            $"Issue #{issue.Number} closes once the commit reaches the default branch"), TipSeverity.Info));
    }

    /// <summary>
    /// Подставляет сообщение по изменённому коду: заголовок и тип — только если заголовок пуст (набранное не стирается),
    /// список изменений — в тело (дописывается к уже введённому).
    /// </summary>
    private async Task SuggestAsync()
    {
        ShowTip(new CommitTip($"{Theme.SpinnerFrames[0]} {T("Смотрю, что изменилось в коде…", "Looking at the code changes…")}", TipSeverity.Info));
        CommitSuggestion? suggestion;
        try
        {
            suggestion = await _suggest!(CancellationToken.None);
        }
        catch (Git.GitCommandException ex)
        {
            App?.Invoke(() => ShowTip(new CommitTip(GitErrors.FirstLine(ex.Result.StdErr), TipSeverity.Error)));
            return;
        }

        App?.Invoke(() =>
        {
            if (suggestion is null)
            {
                ShowTip(new CommitTip(T("В staged C#-файлах нет изменений классов и методов", "No class or method changes in staged C# files"), TipSeverity.Warning));
                return;
            }

            if (_subject.Text.Trim().Length == 0)
            {
                _types.Select(suggestion.Type);
                _subject.Text = suggestion.Subject;
                _subject.InsertionPoint = _subject.Text.Length;
            }

            if (suggestion.Body.Length > 0)
            {
                var current = _body.Text.TrimEnd();
                _body.Text = current.Length == 0 ? suggestion.Body : current + "\n\n" + suggestion.Body;
            }

            UpdatePreview();
            ShowTip(new CommitTip(T("Заполнено по изменённому коду — поправь под себя", "Filled from the code changes: adjust as you like"), TipSeverity.Info));
        });
    }

    protected override bool OnKeyDown(Key key)
    {
        if (HandleWindowKey(key))
            return true;

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

        if (key == Key.T.WithCtrl)
        {
            ApplyTemplate();
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

    /// <summary>Предлагает шаблон и подставляет его в тип, заголовок и тело; курсор встаёт в конец заголовка.</summary>
    private void ApplyTemplate()
    {
        using var picker = new CommitTemplatePicker(CommitTemplate.BuiltIn);
        App?.Run(picker);
        if (picker.Result is not { } template)
            return;

        _types.Select(template.Type);
        _subject.Text = template.Subject;
        _subject.InsertionPoint = _subject.Text.Length;
        _body.Text = template.Body;
        UpdatePreview();
        _subject.SetFocus();
    }

    private string Message => CommitMessage.Compose(_types.Selected, _subject.Text, _body.Text);

    private void Submit()
    {
        if (CommitMessage.Compose(_types.Selected, _subject.Text).Length == 0)
        {
            ShowTip(new CommitTip(T("Введи заголовок коммита", "Enter the commit subject"), TipSeverity.Error));
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

        // Строки тела считаются и без заголовка: подставляем временный заголовок.
        var withBody = CommitMessage.Compose(null, "_", _body.Text);
        var bodyLines = withBody.Length > 1 ? withBody[3..].Split('\n').Length : 0;
        _bodyLabel.Text = bodyLines > 0
            ? T($"Тело коммита · {LinesWord(bodyLines)}", $"Commit body · {LinesWord(bodyLines)}")
            : T("Тело коммита (необязательно)", "Commit body (optional)");
        _bodyLabel.SetScheme(Theme.Text(Theme.Muted));

        var tips = CommitMessage.Check(_types.Selected, _subject.Text).Concat(CommitMessage.CheckBody(_body.Text)).ToList();
        ShowTip(tips.Count > 0 ? tips[0] : null);
    }

    private void UpdateFocusHints()
    {
        _hints.SetHints(_body.HasFocus
            ?
            [
                new Hint("ctrl+s", T("коммит", "commit")),
                new Hint("enter", T("строка", "line")),
                new Hint("↑", T("заголовок", "subject")),
                new Hint("tab", T("тип", "type")),
                new Hint("esc", T("отмена", "cancel")),
            ]
            :
            [
                new Hint("enter", T("коммит", "commit")),
                new Hint("↓", T("тело", "body")),
                new Hint("tab", T("тип", "type")),
                new Hint("ctrl+t", T("шаблон", "template")),
                new Hint("ctrl+e", T("по коду", "from code"), Optional: true),
                .. _issues is null ? Array.Empty<Hint>() : [new Hint("ctrl+k", "issue", Optional: true)],
                new Hint("ctrl+c/x/v", T("буфер", "clipboard")),
                new Hint("esc", T("отмена", "cancel")),
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

    private static string FilesWord(int count) => Plural(count, "файл", "файла", "файлов", "file", "files");

    private static string LinesWord(int count) => Plural(count, "строка", "строки", "строк", "line", "lines");

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

        /// <summary>Выбирает тип по имени; неизвестный тип и null — «без типа».</summary>
        public void Select(string? type)
        {
            var index = Array.IndexOf(CommitMessage.Types, type);
            _index = index < 0 ? CommitMessage.Types.Length - 1 : index;
            SetNeedsDraw();
            Changed?.Invoke(this, EventArgs.Empty);
        }

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
                var name = CommitMessage.Types[i] ?? T("без типа", "no type");
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
