using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>
/// Панель-список с выбором строки: ↑↓ / j k, Home / End, PageUp / PageDown.
/// Наследник рисует строку и задаёт, как узнать тот же элемент после обновления списка.
/// </summary>
public abstract class ListPanel<T> : View where T : class
{
    private IReadOnlyList<T> _items = [];
    private int _selected = -1;
    private int _scroll;

    protected ListPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
    }

    public IReadOnlyList<T> Items => _items;

    /// <summary>Выбранный элемент или null, если список пуст.</summary>
    public T? Selected => _selected >= 0 ? _items[_selected] : null;

    public event EventHandler? SelectionChanged;

    /// <summary>Текст по центру пустой панели.</summary>
    protected abstract string EmptyText { get; }

    /// <summary>Ключ элемента: по нему выбор сохраняется после обновления списка.</summary>
    protected abstract string KeyOf(T item);

    /// <summary>Рисует строку; фон выбранной строки уже закрашен.</summary>
    protected abstract void DrawItem(int line, T item, bool selected, Color? background);

    /// <summary>Сохранять ли прежний выбор в новом списке. Если нет — выбирается <see cref="DefaultIndex"/>.</summary>
    protected virtual bool KeepSelection(IReadOnlyList<T> items) => true;

    /// <summary>Индекс строки, выбираемой в новом списке, если прежнего выбора нет. По умолчанию — первая.</summary>
    protected virtual int DefaultIndex(IReadOnlyList<T> items) => items.Count > 0 ? 0 : -1;

    /// <summary>Обновляет список, по возможности сохраняя выбранный элемент.</summary>
    public void SetItems(IReadOnlyList<T> items)
    {
        var previous = Selected is { } item ? KeyOf(item) : null;
        _items = items;

        var same = -1;
        var keep = KeepSelection(items);
        for (var i = 0; i < items.Count && previous is not null && keep; i++)
        {
            if (KeyOf(items[i]) == previous)
            {
                same = i;
                break;
            }
        }

        _selected = same >= 0 ? same : DefaultIndex(items);
        EnsureVisible();
        SetNeedsDraw();
        if ((Selected is { } current ? KeyOf(current) : null) != previous)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.J)
            return Select(_selected + 1);
        if (key == Key.CursorUp || key == Key.K)
            return Select(_selected - 1);
        if (key == Key.PageDown)
            return Select(_selected + Math.Max(1, Viewport.Height - 1));
        if (key == Key.PageUp)
            return Select(_selected - Math.Max(1, Viewport.Height - 1));
        if (key == Key.Home)
            return Select(0);
        if (key == Key.End)
            return Select(_items.Count - 1);

        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_items.Count == 0)
        {
            var text = EmptyText;
            this.DrawSegments(Math.Max(0, (Viewport.Width - text.Length) / 2), Viewport.Height / 2,
                [new Segment(text, Theme.Muted)], Viewport.Width);
            return true;
        }

        for (var line = 0; line < Viewport.Height; line++)
        {
            var index = _scroll + line;
            if (index >= _items.Count)
                break;

            var selected = index == _selected;
            Color? background = null;
            if (selected)
            {
                SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
                AddStr(0, line, new string(' ', Viewport.Width));
                background = Theme.SelectionBg;
            }

            DrawItem(line, _items[index], selected, background);
        }

        return true;
    }

    /// <summary>Маркер выбранной строки «❯ » или два пробела.</summary>
    protected static Segment Marker(bool selected) =>
        new(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold);

    private bool Select(int index)
    {
        if (_items.Count == 0)
            return true;

        index = Math.Clamp(index, 0, _items.Count - 1);
        if (index == _selected)
            return true;

        _selected = index;
        EnsureVisible();
        SetNeedsDraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void EnsureVisible()
    {
        var height = Math.Max(1, Viewport.Height);
        if (_selected < 0)
            _scroll = 0;
        else if (_selected < _scroll)
            _scroll = _selected;
        else if (_selected >= _scroll + height)
            _scroll = _selected - height + 1;
    }
}
