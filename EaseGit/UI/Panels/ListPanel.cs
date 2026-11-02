using EaseGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace EaseGit.UI.Panels;

/// <summary>
/// Панель-список с выбором строки: ↑↓ / j k, Home / End, PageUp / PageDown, колесо мыши.
/// Наследник рисует строку и задаёт, как узнать тот же элемент после обновления списка.
/// Наследник с <see cref="Matches"/> фильтруется по «/» (<see cref="Filter"/>): видны только подходящие строки,
/// а <see cref="Items"/> остаётся полным списком.
/// </summary>
public abstract class ListPanel<T> : View where T : class
{
    private IReadOnlyList<T> _all = [];
    private IReadOnlyList<T> _items = [];
    private int _selected = -1;
    private int _scroll;

    protected ListPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
        Filter.Changed += (_, _) => Show(_all, keepSelection: true);
    }

    /// <summary>Все элементы, без учёта фильтра.</summary>
    public IReadOnlyList<T> Items => _all;

    /// <summary>Показанные элементы: с фильтром — только подходящие.</summary>
    public IReadOnlyList<T> Shown => _items;

    /// <summary>Фильтр по «/». Работает, только если наследник переопределил <see cref="Matches"/>.</summary>
    public PanelFilter Filter { get; } = new();

    /// <summary>Подходит ли элемент под текст фильтра; по умолчанию панель не фильтруется.</summary>
    protected virtual bool Matches(T item, string filter) => true;

    /// <summary>Номер выбранной строки среди показанных с нуля или -1, если список пуст.</summary>
    public int SelectedIndex => _selected;

    /// <summary>Выбирает строку по номеру (с ограничением границами списка), например чтобы вернуться на прежнее место.</summary>
    public void SelectIndex(int index) => Select(index);

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
    public void SetItems(IReadOnlyList<T> items) => Show(items, KeepSelection(items));

    /// <summary>Показывает <paramref name="all"/> с учётом фильтра; при смене фильтра выбранный элемент сохраняется, если виден.</summary>
    private void Show(IReadOnlyList<T> all, bool keepSelection)
    {
        var previous = Selected is { } item ? KeyOf(item) : null;
        _all = all;
        var items = Filter.Active ? all.Where(i => Matches(i, Filter.Text)).ToList() : all;
        _items = items;

        var same = -1;
        var keep = keepSelection;
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
        if (Filter.HandleKey(key))
            return true;

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

    protected override bool OnMouseEvent(Mouse mouse)
    {
        var step = mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1 : mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? -1 : 0;
        if (step == 0)
            return base.OnMouseEvent(mouse);

        Select(_selected + step);
        return true;
    }

    /// <summary>Строк за один щелчок колеса в панелях просмотра (diff, лог ветки).</summary>
    public const int WheelStep = 3;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_items.Count == 0)
        {
            var text = Filter.Active && _all.Count > 0
                ? T($"Ничего не найдено по «{Filter.Text}» · esc — сбросить", $"Nothing matches '{Filter.Text}' · esc clears")
                : EmptyText;
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

    /// <summary>Уход фокуса с панели завершает набор фильтра, текст остаётся.</summary>
    protected override void OnHasFocusChanged(bool newHasFocus, View? currentFocused, View? newFocused)
    {
        base.OnHasFocusChanged(newHasFocus, currentFocused, newFocused);
        if (!newHasFocus)
            Filter.Stop();
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
