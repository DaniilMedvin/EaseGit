using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>
/// Панель изменённых файлов, сгруппированных по разделам (конфликты, staged, изменения, untracked).
/// Навигация: ↑↓ / j k, Home / End, PageUp / PageDown. Заголовки разделов не выбираются.
/// </summary>
public sealed class FilesPanel : View
{
    private IReadOnlyList<FileRow> _rows = [];
    private int _selected = -1;
    private int _scroll;

    public FilesPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Accent));
    }

    /// <summary>Выбранный файл или null, если изменений нет.</summary>
    public FileEntryRow? SelectedFile => _selected >= 0 ? _rows[_selected] as FileEntryRow : null;

    public event EventHandler? SelectionChanged;

    /// <summary>Обновляет список файлов, по возможности сохраняя выбранный файл.</summary>
    public void SetStatus(RepoStatus status)
    {
        var previous = SelectedFile;
        var previousIndex = _selected;
        _rows = FileRows.Build(status);

        var sameFile = previous is null
            ? -1
            : IndexOf(r => r is FileEntryRow e && e.Section == previous.Section && e.File.Path == previous.File.Path);
        _selected = sameFile >= 0 ? sameFile : NearestFile(Math.Max(previousIndex, 0));

        EnsureVisible();
        SetNeedsDraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.CursorDown || key == Key.J)
            return Move(+1);
        if (key == Key.CursorUp || key == Key.K)
            return Move(-1);
        if (key == Key.PageDown)
            return Move(+Math.Max(1, Viewport.Height - 1));
        if (key == Key.PageUp)
            return Move(-Math.Max(1, Viewport.Height - 1));
        if (key == Key.Home)
            return Select(NearestFile(0));
        if (key == Key.End)
            return Select(NearestFile(_rows.Count - 1, searchBackward: true));

        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_rows.Count == 0)
        {
            const string text = "✓ Нет изменений";
            this.DrawSegments(Math.Max(0, (Viewport.Width - text.Length) / 2), Viewport.Height / 2,
                [new Segment(text, Theme.Success)], Viewport.Width);
            return true;
        }

        for (var line = 0; line < Viewport.Height; line++)
        {
            var index = _scroll + line;
            if (index >= _rows.Count)
                break;

            switch (_rows[index])
            {
                case SectionHeaderRow header:
                    DrawHeader(line, header);
                    break;
                case FileEntryRow file:
                    DrawFile(line, file, index == _selected);
                    break;
            }
        }

        return true;
    }

    private void DrawHeader(int line, SectionHeaderRow header)
    {
        var (title, color) = header.Section switch
        {
            FileSection.Conflicts => ("Конфликты", Theme.Error),
            FileSection.Staged => ("Staged", Theme.Success),
            FileSection.Unstaged => ("Изменения", Theme.Warning),
            _ => ("Untracked", Theme.Muted),
        };
        this.DrawSegments(1, line,
        [
            new Segment(title, color, TextStyle.Bold),
            new Segment($" {header.Count}", Theme.Dim),
        ], Viewport.Width - 1);
    }

    private void DrawFile(int line, FileEntryRow row, bool selected)
    {
        var background = selected ? Theme.SelectionBg : (Color?)null;
        if (selected)
        {
            SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
            AddStr(0, line, new string(' ', Viewport.Width));
        }

        var (symbol, color) = Theme.ForChange(row.Kind);
        var path = row.File.OriginalPath is { } original && row.Section == FileSection.Staged
            ? $"{original} → {row.File.Path}"
            : row.File.Path;

        this.DrawSegments(1, line,
        [
            new Segment(selected ? Theme.SymbolSelection : " ", Theme.Accent, TextStyle.Bold),
            new Segment($" {symbol} ", color),
            new Segment(path, selected ? Theme.Fg : color),
        ], Viewport.Width - 1, background);
    }

    private bool Move(int delta)
    {
        if (_selected < 0)
            return true;

        var target = Math.Clamp(_selected + delta, 0, _rows.Count - 1);
        return Select(NearestFile(target, searchBackward: delta < 0));
    }

    private bool Select(int index)
    {
        if (index < 0 || index == _selected)
            return true;

        _selected = index;
        EnsureVisible();
        SetNeedsDraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Ближайшая к <paramref name="start"/> строка с файлом в заданном направлении (затем в обратном).</summary>
    private int NearestFile(int start, bool searchBackward = false)
    {
        if (_rows.Count == 0)
            return -1;

        start = Math.Clamp(start, 0, _rows.Count - 1);
        var step = searchBackward ? -1 : 1;
        for (var i = start; i >= 0 && i < _rows.Count; i += step)
        {
            if (_rows[i] is FileEntryRow)
                return i;
        }

        for (var i = start; i >= 0 && i < _rows.Count; i -= step)
        {
            if (_rows[i] is FileEntryRow)
                return i;
        }

        return -1;
    }

    private int IndexOf(Func<FileRow, bool> predicate)
    {
        for (var i = 0; i < _rows.Count; i++)
        {
            if (predicate(_rows[i]))
                return i;
        }

        return -1;
    }

    private void EnsureVisible()
    {
        var height = Math.Max(1, Viewport.Height);
        if (_selected < 0)
        {
            _scroll = 0;
            return;
        }

        // Заголовок раздела над первым файлом тоже остаётся видимым.
        var top = _selected > 0 && _rows[_selected - 1] is SectionHeaderRow ? _selected - 1 : _selected;
        if (top < _scroll)
            _scroll = top;
        else if (_selected >= _scroll + height)
            _scroll = _selected - height + 1;
    }
}
