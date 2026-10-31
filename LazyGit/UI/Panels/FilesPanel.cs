using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>
/// Панель изменённых файлов, сгруппированных по разделам (конфликты, staged, изменения, новые файлы).
/// Внутри раздела файлы показаны деревом папок. Вид изменения — плашка с буквой (M, A, D, R, U, !),
/// заголовок раздела — мягкая плашка.
/// Навигация: ↑↓ / j k, Home / End, PageUp / PageDown. Заголовки разделов не выбираются.
/// Папки: enter — свернуть / развернуть, ← / h — свернуть или перейти к родительской папке, → / l — развернуть.
/// </summary>
public sealed class FilesPanel : View
{
    private readonly HashSet<(FileSection, string)> _collapsed = [];
    private RepoStatus? _status;
    private IReadOnlyList<FileEntryRow> _entries = [];
    private IReadOnlyList<FileRow> _rows = [];
    private int _selected = -1;
    private int _scroll;

    public FilesPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
    }

    /// <summary>Все изменённые файлы по разделам, включая файлы в свёрнутых папках.</summary>
    public IReadOnlyList<FileEntryRow> Entries => _entries;

    /// <summary>Выбранная строка (файл или папка) или null, если изменений нет.</summary>
    public FileRow? SelectedRow => _selected >= 0 ? _rows[_selected] : null;

    /// <summary>Выбранный файл или null, если выбрана папка или изменений нет.</summary>
    public FileEntryRow? SelectedFile => SelectedRow as FileEntryRow;

    /// <summary>Выбранная папка или null.</summary>
    public DirectoryRow? SelectedDirectory => SelectedRow as DirectoryRow;

    public event EventHandler? SelectionChanged;

    /// <summary>Обновляет список файлов, по возможности сохраняя выбранную строку.</summary>
    public void SetStatus(RepoStatus status)
    {
        _status = status;
        _entries = FileRows.Entries(status);
        Rebuild();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Перестраивает строки. Если выбранная строка спрятана в свёрнутую папку, выбирается эта папка,
    /// если исчезла совсем — ближайшая строка на прежнем месте.
    /// </summary>
    private void Rebuild()
    {
        if (_status is null)
            return;

        var previous = SelectedRow;
        var previousIndex = _selected;
        _rows = FileRows.Build(_status, _collapsed);

        var index = previous switch
        {
            FileEntryRow f => IndexOf(r => r is FileEntryRow e && e.Section == f.Section && e.File.Path == f.File.Path),
            DirectoryRow d => IndexOf(r => r is DirectoryRow e && e.Section == d.Section && e.Path == d.Path),
            _ => -1,
        };
        if (index < 0 && previous is not null)
            index = ContainingDirectory(previous);

        _selected = index >= 0 ? index : NearestSelectable(Math.Max(previousIndex, 0));
        EnsureVisible();
        SetNeedsDraw();
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
            return Select(NearestSelectable(0));
        if (key == Key.End)
            return Select(NearestSelectable(_rows.Count - 1, searchBackward: true));
        if (key == Key.Enter && SelectedDirectory is { } directory)
            return SetCollapsed(directory, !directory.Collapsed);
        if (key == Key.CursorRight || key == Key.L)
            return SelectedDirectory is { Collapsed: true } closed ? SetCollapsed(closed, false) : true;
        if (key == Key.CursorLeft || key == Key.H)
        {
            if (SelectedDirectory is { Collapsed: false } open)
                return SetCollapsed(open, true);
            return Select(ParentDirectory());
        }

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
                case DirectoryRow directory:
                    DrawDirectory(line, directory, index == _selected);
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
            _ => ("Новые файлы", Theme.Cyan),
        };
        this.DrawSegments(1, line,
        [
            Segment.Pill(title, color, TextStyle.Bold),
            new Segment($" {header.Count}", Theme.Dim),
        ], Viewport.Width - 1);
    }

    private void DrawDirectory(int line, DirectoryRow row, bool selected)
    {
        var background = DrawSelection(line, selected);
        var symbol = row.Collapsed ? Theme.SymbolCollapsed : Theme.SymbolExpanded;
        var count = row.Collapsed ? $" {row.Files.Count}" : "";

        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(Indent(row.Depth) + symbol + " ", Theme.Dim),
            new Segment(row.Name + "/", selected ? Theme.Fg : Theme.Muted),
            new Segment(count, Theme.Dim),
        ], Viewport.Width - 1, background);
    }

    private void DrawFile(int line, FileEntryRow row, bool selected)
    {
        var background = DrawSelection(line, selected);
        var (letter, color) = Theme.ForChange(row.Kind);

        this.DrawSegments(1, line,
        [
            Marker(selected),
            new Segment(Indent(row.Depth), Theme.Fg),
            Segment.Badge(letter, color),
            new Segment(" " + DisplayName(row), selected ? Theme.Fg : color),
        ], Viewport.Width - 1, background);
    }

    /// <summary>Имя файла; для переименования в Staged — «old → new», старый путь целиком, если папка другая.</summary>
    private static string DisplayName(FileEntryRow row)
    {
        if (row.File.OriginalPath is not { } original || row.Section != FileSection.Staged)
            return row.Name;

        var sameFolder = Path.GetDirectoryName(original) == Path.GetDirectoryName(row.File.Path);
        return $"{(sameFolder ? Path.GetFileName(original) : original)} → {row.Name}";
    }

    /// <summary>Закрашивает фон выбранной строки. Возвращает фон для фрагментов.</summary>
    private Color? DrawSelection(int line, bool selected)
    {
        if (!selected)
            return null;

        SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
        AddStr(0, line, new string(' ', Viewport.Width));
        return Theme.SelectionBg;
    }

    private static Segment Marker(bool selected) =>
        new(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold);

    private static string Indent(int depth) => new(' ', depth * 2);

    private bool SetCollapsed(DirectoryRow directory, bool collapsed)
    {
        if (collapsed)
            _collapsed.Add((directory.Section, directory.Path));
        else
            _collapsed.Remove((directory.Section, directory.Path));

        Rebuild();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Индекс папки, в которой лежит выбранная строка, или -1 для строки верхнего уровня.</summary>
    private int ParentDirectory()
    {
        var depth = SelectedRow switch
        {
            DirectoryRow d => d.Depth,
            FileEntryRow f => f.Depth,
            _ => 0,
        };
        for (var i = _selected - 1; i >= 0 && depth > 0; i--)
        {
            if (_rows[i] is SectionHeaderRow)
                return -1;
            if (_rows[i] is DirectoryRow d && d.Depth < depth)
                return i;
        }

        return -1;
    }

    /// <summary>Индекс самой глубокой видимой папки, в которой лежит прежняя строка, или -1.</summary>
    private int ContainingDirectory(FileRow row)
    {
        var (section, path) = row switch
        {
            DirectoryRow d => (d.Section, d.Path),
            FileEntryRow f => (f.Section, f.File.Path),
            _ => (default, ""),
        };

        var found = -1;
        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i] is DirectoryRow d && d.Section == section && path.StartsWith(d.Path + "/", StringComparison.Ordinal))
                found = i;
        }

        return found;
    }

    private bool Move(int delta)
    {
        if (_selected < 0)
            return true;

        var target = Math.Clamp(_selected + delta, 0, _rows.Count - 1);
        return Select(NearestSelectable(target, searchBackward: delta < 0));
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

    /// <summary>Ближайшая к <paramref name="start"/> строка с файлом или папкой в заданном направлении (затем в обратном).</summary>
    private int NearestSelectable(int start, bool searchBackward = false)
    {
        if (_rows.Count == 0)
            return -1;

        start = Math.Clamp(start, 0, _rows.Count - 1);
        var step = searchBackward ? -1 : 1;
        for (var i = start; i >= 0 && i < _rows.Count; i += step)
        {
            if (_rows[i] is not SectionHeaderRow)
                return i;
        }

        for (var i = start; i >= 0 && i < _rows.Count; i -= step)
        {
            if (_rows[i] is not SectionHeaderRow)
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
