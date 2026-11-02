using EaseGit.Models;
using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace EaseGit.UI.Dialogs;

/// <summary>
/// Список шаблонов сообщения коммита: ↑↓ — выбор, enter — применить, esc — закрыть.
/// Под списком показан заголовок, который получится. Result — выбранный шаблон или null.
/// </summary>
public sealed class CommitTemplatePicker : Runnable<CommitTemplate?>
{
    private const int DialogWidth = 60;

    private readonly IReadOnlyList<CommitTemplate> _templates;
    private readonly TemplateList _list;
    private readonly Label _preview;

    public CommitTemplatePicker(IReadOnlyList<CommitTemplate> templates)
    {
        _templates = templates;
        var listHeight = Math.Min(templates.Count, 12);
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = listHeight + 6;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = listHeight + 5,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        _list = new TemplateList(templates) { X = 0, Y = 0, Width = Dim.Fill(), Height = listHeight };
        _list.SelectionChanged += (_, _) => UpdatePreview();

        _preview = new Label { X = 0, Y = listHeight + 1, Width = Dim.Fill() };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("применить", "apply")), new Hint("esc", T("закрыть", "close"))]);

        box.Add(_list, _preview);
        Add(box, BoxTitle.For(box, T("Шаблоны коммита", "Commit templates"), Theme.Accent), hints);
        UpdatePreview();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.N.WithCtrl || key == Key.J)
            return _list.Move(1);
        if (key == Key.CursorUp || key == Key.P.WithCtrl || key == Key.K)
            return _list.Move(-1);
        if (key == Key.Enter)
        {
            Result = _templates[_list.Selected];
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    private void UpdatePreview()
    {
        _preview.Text = _templates[_list.Selected].Title;
        _preview.SetScheme(Theme.Text(Theme.Info));
    }

    private sealed class TemplateList : View
    {
        private readonly IReadOnlyList<CommitTemplate> _items;
        private int _scroll;

        public TemplateList(IReadOnlyList<CommitTemplate> items) => _items = items;

        public int Selected { get; private set; }

        public event EventHandler? SelectionChanged;

        public bool Move(int delta)
        {
            Selected = Math.Clamp(Selected + delta, 0, _items.Count - 1);
            if (Selected < _scroll)
                _scroll = Selected;
            else if (Selected >= _scroll + Viewport.Height)
                _scroll = Selected - Viewport.Height + 1;
            SetNeedsDraw();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            return true;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            for (var row = 0; row < Viewport.Height && _scroll + row < _items.Count; row++)
            {
                var index = _scroll + row;
                var item = _items[index];
                var selected = index == Selected;
                Color? background = null;
                if (selected)
                {
                    SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
                    AddStr(0, row, new string(' ', Viewport.Width));
                    background = Theme.SelectionBg;
                }

                var type = item.Type ?? (CommitMessage.HasTypePrefix(item.Subject) ? item.Subject.Split(':')[0] : "");
                var typeX = Viewport.Width - type.Length - 1;
                this.DrawSegments(0, row,
                [
                    new Segment(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold),
                    new Segment(item.Name, selected ? Theme.Fg : Theme.Muted),
                ], Math.Max(0, typeX - 1), background);
                this.DrawSegments(typeX, row, [new Segment(type, Theme.Dim)], type.Length, background);
            }

            return true;
        }
    }
}
