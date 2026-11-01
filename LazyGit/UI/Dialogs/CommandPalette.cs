using LazyGit.Commands;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Палитра команд (ctrl+p или :), как в opencode: поиск по всем доступным командам, клавиша справа,
/// под списком — git-команда выбранного действия. ↑↓ — выбор, enter — выполнить, esc — закрыть.
/// Result — выбранная команда или null.
/// </summary>
public sealed class CommandPalette : Runnable<AppCommand?>
{
    private const int DialogWidth = 64;
    private const int ListHeight = 12;

    private readonly IReadOnlyList<AppCommand> _commands;
    private readonly ClipboardTextField _query;
    private readonly CommandList _list;
    private readonly Label _preview;

    public CommandPalette(IReadOnlyList<AppCommand> commands)
    {
        _commands = commands;
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = ListHeight + 7;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = ListHeight + 6,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var prompt = new Label { Text = "> ", X = 0, Y = 0 };
        prompt.SetScheme(Theme.Text(Theme.Accent, TextStyle.Bold));

        _query = new ClipboardTextField { X = Pos.Right(prompt), Y = 0, Width = Dim.Fill() };
        _query.SetScheme(Theme.Input);
        _query.TextChanged += (_, _) => Filter();
        _query.Accepting += (_, e) =>
        {
            e.Handled = true;
            Submit();
        };

        _list = new CommandList { X = 0, Y = 2, Width = Dim.Fill(), Height = ListHeight };
        _list.SelectionChanged += (_, _) => UpdatePreview();

        _preview = new Label { X = 0, Y = ListHeight + 3, Width = Dim.Fill() };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("выполнить", "run")), new Hint("esc", T("закрыть", "close"))]);

        box.Add(prompt, _query, _list, _preview);
        Add(box, BoxTitle.For(box, T("Команды", "Commands"), Theme.Accent), hints);
        Filter();
        _query.SetFocus();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.N.WithCtrl)
            return _list.Move(1);
        if (key == Key.CursorUp || key == Key.P.WithCtrl)
            return _list.Move(-1);
        if (key == Key.PageDown)
            return _list.Move(ListHeight - 1);
        if (key == Key.PageUp)
            return _list.Move(-(ListHeight - 1));

        return base.OnKeyDown(key);
    }

    private void Filter()
    {
        _list.SetItems(CommandSearch.Filter(_commands, _query.Text));
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var preview = _list.Selected?.Preview?.Invoke();
        _preview.Text = _list.Selected is null ? T("Ничего не найдено", "Nothing found") : preview ?? "";
        _preview.SetScheme(Theme.Text(_list.Selected is null ? Theme.Muted : Theme.Info));
    }

    private void Submit()
    {
        if (_list.Selected is not { } command)
            return;

        Result = command;
        RequestStop();
    }

    /// <summary>Список найденных команд: название, панель и клавиша справа; опасные отмечены «⚠».</summary>
    private sealed class CommandList : View
    {
        private IReadOnlyList<AppCommand> _items = [];
        private int _selected;
        private int _scroll;

        public AppCommand? Selected => _selected < _items.Count ? _items[_selected] : null;

        public event EventHandler? SelectionChanged;

        public void SetItems(IReadOnlyList<AppCommand> items)
        {
            _items = items;
            _selected = 0;
            _scroll = 0;
            SetNeedsDraw();
        }

        public bool Move(int delta)
        {
            if (_items.Count == 0)
                return true;

            _selected = Math.Clamp(_selected + delta, 0, _items.Count - 1);
            if (_selected < _scroll)
                _scroll = _selected;
            else if (_selected >= _scroll + Viewport.Height)
                _scroll = _selected - Viewport.Height + 1;
            SetNeedsDraw();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            for (var row = 0; row < Viewport.Height && _scroll + row < _items.Count; row++)
            {
                var index = _scroll + row;
                var command = _items[index];
                var selected = index == _selected;
                Color? background = null;
                if (selected)
                {
                    SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
                    AddStr(0, row, new string(' ', Viewport.Width));
                    background = Theme.SelectionBg;
                }

                var key = (command.IsDangerous ? Theme.SymbolWarning + " " : "") + command.Key.Label;
                var keyX = Viewport.Width - key.Length - 1;
                this.DrawSegments(0, row,
                [
                    new Segment(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold),
                    new Segment(command.Title, selected ? Theme.Fg : Theme.Muted),
                    new Segment("  " + ScopeName(command.Scope), Theme.Dim),
                ], Math.Max(0, keyX - 1), background);
                this.DrawSegments(keyX, row, [new Segment(key, command.IsDangerous ? Theme.Warning : Theme.Fg)], key.Length, background);
            }

            return true;
        }

        private static string ScopeName(CommandScope scope) => scope switch
        {
            CommandScope.Files => T("файлы", "files"),
            CommandScope.Branches => T("ветки", "branches"),
            CommandScope.Commits => T("коммиты", "commits"),
            _ => "",
        };
    }
}
