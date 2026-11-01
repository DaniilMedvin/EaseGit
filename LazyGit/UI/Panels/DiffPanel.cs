using LazyGit.Config;
using LazyGit.Highlighting;
using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>Diff, подготовленный к показу: без служебных строк и с подсветкой синтаксиса.</summary>
/// <param name="Source">
/// Исходный diff одного файла (со служебными строками) — из него строится патч для построчного stage.
/// Строки изменений в <paramref name="Lines"/> — те же объекты, что в Source. null — diff коммита или stash.
/// </param>
public sealed record PreparedDiff(IReadOnlyList<DiffLine> Lines, IReadOnlyList<HighlightSpan>?[] Highlights,
    IReadOnlyList<DiffLine>? Source = null);

/// <summary>
/// Diff выбранного файла: номера строк, «+» и «-» на цветном фоне, заголовки фрагментов.
/// Служебные строки git (diff --git, index, ---, +++) скрыты, важные показываются коротко по-русски.
/// Для C# код подсвечивается в стиле тёмной темы Visual Studio.
/// В режиме построчного stage панель получает фокус: курсор ходит по строкам «+» и «-» (↑↓ / j k, PageUp / PageDown,
/// Home / End), выделение нескольких строк — <see cref="ToggleRange"/>.
/// </summary>
public sealed class DiffPanel : View
{
    private const int TabWidth = 4;

    private IReadOnlyList<DiffLine> _lines = [];
    private IReadOnlyList<HighlightSpan>?[] _highlights = [];
    private string? _message;
    private int _scroll;
    private int _numberWidth = 3;
    private Mascot _mascot;
    private IReadOnlyList<DiffLine>? _source;
    private bool _staging;
    private int _cursor = -1;
    private int? _anchor;

    public DiffPanel()
    {
        CanFocus = false;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    /// <summary>
    /// Убирает служебные строки и подсвечивает синтаксис. Подсветка заметно нагружает процессор,
    /// поэтому метод вызывается вне потока интерфейса.
    /// </summary>
    public static PreparedDiff Prepare(IReadOnlyList<DiffLine> lines, string path)
    {
        var simplified = lines.Select(Simplify).OfType<DiffLine>().ToList();
        return new PreparedDiff(simplified, DiffHighlighter.Highlight(simplified, path), lines);
    }

    /// <summary>
    /// Подготовка коммита: строка «хеш · автор · дата», сообщение, затем изменения каждого файла
    /// под заголовком с путём. Подсветка синтаксиса — по расширению каждого файла.
    /// </summary>
    public static PreparedDiff PrepareCommit(Commit commit, CommitDiff diff, DateTimeOffset now)
    {
        var lines = new List<DiffLine>();
        var highlights = new List<IReadOnlyList<HighlightSpan>?>();
        void Add(DiffLine line)
        {
            lines.Add(line);
            highlights.Add(null);
        }

        var date = commit.Date.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
        var age = RelativeTime.Format(commit.Date, now);
        var ago = age == "сейчас" ? "только что" : $"{age} назад";
        Add(new DiffLine(DiffLineKind.Header,
            $"{commit.ShortHash} {Theme.SymbolSeparator} {commit.AuthorName} {Theme.SymbolSeparator} {date} ({ago})"));
        foreach (var text in diff.Message.Split('\n'))
            Add(new DiffLine(DiffLineKind.Message, text.TrimEnd('\r')));

        var files = SplitByFile(diff.Lines);
        foreach (var file in files)
        {
            var path = FilePath(file);
            Add(new DiffLine(DiffLineKind.Message, ""));
            Add(new DiffLine(DiffLineKind.File, path));
            var prepared = Prepare(file, path);
            lines.AddRange(prepared.Lines);
            highlights.AddRange(prepared.Highlights);
        }

        if (files.Count == 0)
        {
            Add(new DiffLine(DiffLineKind.Message, ""));
            Add(new DiffLine(DiffLineKind.Header, "В коммите нет изменений файлов"));
        }

        return new PreparedDiff(lines, highlights.ToArray());
    }

    /// <summary>Делит diff нескольких файлов на части по строкам «diff --git». Строки до первой отбрасываются.</summary>
    private static List<List<DiffLine>> SplitByFile(IReadOnlyList<DiffLine> lines)
    {
        var files = new List<List<DiffLine>>();
        foreach (var line in lines)
        {
            if (line.Kind == DiffLineKind.Header && line.Text.StartsWith("diff --"))
                files.Add([]);
            files.LastOrDefault()?.Add(line);
        }

        return files;
    }

    /// <summary>
    /// Путь файла из служебных строк: «+++ b/…», для переименования — «rename to …»,
    /// для удалённого — «--- a/…», для бинарного — из «diff --git a/… b/…».
    /// </summary>
    private static string FilePath(IReadOnlyList<DiffLine> file)
    {
        string? renamed = null, deleted = null;
        foreach (var line in file.Where(l => l.Kind == DiffLineKind.Header))
        {
            // Если в пути есть пробел, git дописывает в строки ---/+++ табуляцию.
            var text = line.Text.TrimEnd('\t');
            if (text.StartsWith("+++ b/"))
                return text[6..];
            if (text.StartsWith("rename to "))
                renamed = text["rename to ".Length..];
            else if (text.StartsWith("--- a/"))
                deleted = text[6..];
        }

        if ((renamed ?? deleted) is { } found)
            return found;

        // «diff --git a/P b/P»: без переименования оба пути одинаковы, поэтому P — половина строки.
        var header = file[0].Text["diff --git ".Length..];
        return header.Length > 5 ? header[2..((header.Length - 1) / 2)] : header;
    }

    /// <summary>Показывает diff. Без <paramref name="keepScroll"/> прокручивает в начало.</summary>
    public void SetDiff(PreparedDiff diff, bool keepScroll = false)
    {
        _lines = diff.Lines;
        _highlights = diff.Highlights;
        _source = diff.Source;
        _message = _lines.Count == 0 ? "Нет изменений для показа" : null;
        _scroll = keepScroll ? Math.Clamp(_scroll, 0, Math.Max(0, _lines.Count - Viewport.Height)) : 0;
        if (_staging)
        {
            // После stage строки сдвигаются: курсор встаёт на ближайшее изменение на прежнем месте.
            _anchor = null;
            _cursor = NearestChange(_cursor >= 0 ? _cursor : _scroll);
            EnsureCursorVisible();
        }

        var maxNumber = _lines.Select(l => Math.Max(l.OldNumber ?? 0, l.NewNumber ?? 0)).DefaultIfEmpty(0).Max();
        _numberWidth = Math.Max(3, maxNumber.ToString().Length);
        SetNeedsDraw();
    }

    /// <summary>
    /// Маскот в правом нижнем углу. Рисуется, только если diff до него не доходит и панель достаточно широкая,
    /// поэтому никогда не закрывает текст.
    /// </summary>
    public Mascot Mascot
    {
        get => _mascot;
        set
        {
            _mascot = value;
            SetNeedsDraw();
        }
    }

    /// <summary>Исходный diff показанного файла или null (коммит, stash, сообщение).</summary>
    public IReadOnlyList<DiffLine>? Source => _source;

    /// <summary>Включает режим построчного stage: панель получает фокус, курсор — на первом видимом изменении.</summary>
    public void StartStaging()
    {
        _staging = true;
        _anchor = null;
        _cursor = NearestChange(_scroll);
        EnsureCursorVisible();
        CanFocus = true;
        SetFocus();
        SetNeedsDraw();
    }

    /// <summary>Выключает режим построчного stage. Фокус к этому моменту уже должен быть у другой панели.</summary>
    public void StopStaging()
    {
        _staging = false;
        _anchor = null;
        _cursor = -1;
        CanFocus = false;
        SetNeedsDraw();
    }

    /// <summary>Начинает выделение нескольких строк от курсора или снимает его.</summary>
    public void ToggleRange()
    {
        if (!_staging || _cursor < 0)
            return;

        _anchor = _anchor is null ? _cursor : null;
        SetNeedsDraw();
    }

    public bool HasRange => _anchor is not null;

    /// <summary>Выбранные изменения: строка под курсором или все «+» и «-» выделения.</summary>
    public IReadOnlyList<DiffLine> SelectedChanges
    {
        get
        {
            if (_cursor < 0)
                return [];

            var (from, to) = SelectionBounds();
            return _lines.Skip(from).Take(to - from + 1).Where(IsChange).ToList();
        }
    }

    /// <summary>Все изменения фрагмента (от @@ до следующего @@), в котором стоит курсор.</summary>
    public IReadOnlyList<DiffLine> CurrentHunkChanges
    {
        get
        {
            if (_cursor < 0)
                return [];

            var from = _cursor;
            while (from > 0 && _lines[from - 1].Kind is not (DiffLineKind.Hunk or DiffLineKind.Header))
                from--;
            var to = _cursor;
            while (to + 1 < _lines.Count && _lines[to + 1].Kind is not (DiffLineKind.Hunk or DiffLineKind.Header))
                to++;
            return _lines.Skip(from).Take(to - from + 1).Where(IsChange).ToList();
        }
    }

    private (int From, int To) SelectionBounds() =>
        _anchor is { } anchor ? (Math.Min(anchor, _cursor), Math.Max(anchor, _cursor)) : (_cursor, _cursor);

    private static bool IsChange(DiffLine line) => line.Kind is DiffLineKind.Added or DiffLineKind.Removed;

    /// <summary>Ближайшая к <paramref name="index"/> строка изменения: сначала ниже, потом выше. -1 — изменений нет.</summary>
    private int NearestChange(int index)
    {
        index = Math.Clamp(index, 0, Math.Max(0, _lines.Count - 1));
        for (var i = index; i < _lines.Count; i++)
            if (IsChange(_lines[i]))
                return i;
        for (var i = Math.Min(index, _lines.Count) - 1; i >= 0; i--)
            if (IsChange(_lines[i]))
                return i;
        return -1;
    }

    /// <summary>Сдвигает курсор на <paramref name="steps"/> изменений вниз (или вверх, если меньше нуля).</summary>
    private bool MoveCursor(int steps)
    {
        if (_cursor < 0)
            return true;

        var direction = Math.Sign(steps);
        var target = _cursor;
        for (var i = _cursor + direction; i >= 0 && i < _lines.Count && steps != 0; i += direction)
        {
            if (!IsChange(_lines[i]))
                continue;
            target = i;
            steps -= direction;
        }

        _cursor = target;
        EnsureCursorVisible();
        SetNeedsDraw();
        return true;
    }

    private void EnsureCursorVisible()
    {
        if (_cursor < 0 || Viewport.Height <= 0)
            return;

        // Заголовок фрагмента над первой строкой тоже виден: по нему понятно, где мы в файле.
        var top = _cursor > 0 && _lines[_cursor - 1].Kind == DiffLineKind.Hunk ? _cursor - 1 : _cursor;
        if (top < _scroll)
            _scroll = top;
        else if (_cursor >= _scroll + Viewport.Height)
            _scroll = _cursor - Viewport.Height + 1;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (!_staging)
            return base.OnKeyDown(key);

        var page = Math.Max(1, Viewport.Height - 1);
        if (key == Key.CursorDown || key == Key.J)
            return MoveCursor(+1);
        if (key == Key.CursorUp || key == Key.K)
            return MoveCursor(-1);
        if (key == Key.PageDown)
            return MoveCursor(+page);
        if (key == Key.PageUp)
            return MoveCursor(-page);
        if (key == Key.Home)
            return MoveCursor(-_lines.Count);
        if (key == Key.End)
            return MoveCursor(+_lines.Count);

        return base.OnKeyDown(key);
    }

    /// <summary>Показывает сообщение вместо diff (загрузка, ошибка, нет выбранного файла).</summary>
    public void SetMessage(string message)
    {
        _lines = [];
        _highlights = [];
        _source = null;
        _cursor = -1;
        _anchor = null;
        _message = message;
        _scroll = 0;
        SetNeedsDraw();
    }

    /// <summary>Прокрутка на <paramref name="delta"/> строк; отрицательное значение — вверх.</summary>
    public void ScrollBy(int delta)
    {
        var max = Math.Max(0, _lines.Count - Viewport.Height);
        var scroll = Math.Clamp(_scroll + delta, 0, max);
        if (scroll == _scroll)
            return;

        _scroll = scroll;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_message is not null)
        {
            this.DrawSegments(1, 0, [new Segment(_message, Theme.Muted)], Viewport.Width - 1);
            DrawMascot(usedRows: 1);
            return true;
        }

        for (var row = 0; row < Viewport.Height && _scroll + row < _lines.Count; row++)
            DrawLine(row, _scroll + row);

        DrawMascot(usedRows: Math.Min(Viewport.Height, _lines.Count - _scroll));
        return true;
    }

    private void DrawMascot(int usedRows)
    {
        var x = Viewport.Width - MascotArt.Width - 2;
        var y = Viewport.Height - MascotArt.Rows;
        // Пустая строка между текстом и маскотом; на узкой панели место отдаётся diff.
        if (_mascot == Mascot.None || usedRows >= y || Viewport.Width < MascotArt.Width + 30)
            return;

        MascotArt.Draw(this, x, y, _mascot);
    }

    private void DrawLine(int row, int index)
    {
        var line = _lines[index];
        var highlight = _highlights[index];
        var width = Viewport.Width;
        switch (line.Kind)
        {
            case DiffLineKind.File:
                SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
                AddStr(0, row, new string(' ', width));
                this.DrawSegments(1, row, [new Segment(line.Text, Theme.Fg, TextStyle.Bold)], width - 2, Theme.SelectionBg);
                return;
            case DiffLineKind.Message:
                // Первая строка сообщения — заголовок коммита.
                var subject = index == 0 || _lines[index - 1].Kind != DiffLineKind.Message;
                this.DrawSegments(1, row, [new Segment(line.Text, Theme.Fg, subject ? TextStyle.Bold : TextStyle.None)], width - 1);
                return;
            case DiffLineKind.Header:
            case DiffLineKind.Binary:
                this.DrawSegments(1, row, [new Segment(line.Text, Theme.Muted, TextStyle.Italic)], width - 1);
                return;
            case DiffLineKind.Hunk:
                this.DrawSegments(1, row, [new Segment(line.Text, Theme.Info)], width - 1);
                return;
            case DiffLineKind.NoNewline:
                this.DrawSegments(0, row, [new Segment(new string(' ', _numberWidth * 2 + 3) + "⏎ нет перевода строки в конце файла", Theme.Dim, TextStyle.Italic)], width);
                return;
        }

        var (marker, color, background) = line.Kind switch
        {
            DiffLineKind.Added => ("+", Theme.Success, Theme.AddedBg),
            DiffLineKind.Removed => ("-", Theme.Error, Theme.RemovedBg),
            _ => (" ", Theme.Fg, (Color?)null),
        };
        if (_staging && _cursor >= 0 && SelectionBounds() is var (from, to) && index >= from && index <= to)
            background = Theme.SelectionBg;

        if (background is { } bg)
        {
            SetAttribute(new Attribute(Theme.Fg, bg));
            AddStr(0, row, new string(' ', width));
        }

        var old = line.OldNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
        var @new = line.NewNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
        var segments = new List<Segment>
        {
            new($"{old} {@new} ", Theme.Dim),
            new(marker + " ", color, TextStyle.Bold),
        };
        if (highlight is null)
            segments.Add(new Segment(ExpandTabs(line.Text), line.Kind == DiffLineKind.Context ? Theme.Fg : color));
        else
            segments.AddRange(SyntaxSegments(line.Text, highlight));

        this.DrawSegments(0, row, segments, width, background);
    }

    /// <summary>Разбивает текст строки на цветные фрагменты; промежутки между spans — обычный текст.</summary>
    private static IEnumerable<Segment> SyntaxSegments(string text, IReadOnlyList<HighlightSpan> spans)
    {
        var position = 0;
        foreach (var span in spans)
        {
            if (span.Start > position)
                yield return new Segment(ExpandTabs(text[position..span.Start]), Theme.SyntaxPlain);
            yield return new Segment(ExpandTabs(text.Substring(span.Start, span.Length)), Theme.Syntax(span.Class));
            position = span.End;
        }

        if (position < text.Length)
            yield return new Segment(ExpandTabs(text[position..]), Theme.SyntaxPlain);
    }

    private static string ExpandTabs(string text) => text.Replace("\t", new string(' ', TabWidth));

    /// <summary>Скрывает служебные строки и переводит важные. null — строка не показывается.</summary>
    private static DiffLine? Simplify(DiffLine line)
    {
        if (line.Kind == DiffLineKind.Binary)
            return line with { Text = "Бинарный файл: содержимое не показывается" };
        if (line.Kind != DiffLineKind.Header)
            return line;

        var text = line.Text;
        if (text.StartsWith("new file mode"))
            return line with { Text = "Новый файл" };
        if (text.StartsWith("deleted file mode"))
            return line with { Text = "Файл удалён" };
        if (text.StartsWith("rename from "))
            return line with { Text = "Переименован из " + text["rename from ".Length..] };
        if (text.StartsWith("old mode") || text.StartsWith("new mode"))
            return line with { Text = "Права доступа: " + text };
        return null;
    }
}
