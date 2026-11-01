using System.Text.RegularExpressions;
using LazyGit.Config;
using LazyGit.DotNet;
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
    // Прокрутка — в экранных строках: с переносом одна строка diff может занимать несколько.
    private int _scroll;
    private bool _wrap = true;
    private List<(int Line, int Part, int Start, int Length)> _rows = [];
    private int _rowsWidth = -1;
    private int _numberWidth = 3;

    // Ширина колонки путей и самое большое число изменений среди строк Stat: по ним выравнивается сводка.
    private int _statPathWidth;
    private int _statMaxTotal;
    private int _statCountWidth;
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
        var highlights = DiffHighlighter.Highlight(simplified, path);
        if (!PackageChanges.IsProjectFile(path) || PackageChanges.Detect(lines) is not { Count: > 0 } packages)
            return new PreparedDiff(simplified, highlights, lines);

        // Над diff файла проекта — какие пакеты обновились, добавились, убраны.
        var notes = packages.Select(PackageNote).ToList();
        return new PreparedDiff([.. notes, .. simplified], [.. notes.Select(_ => (IReadOnlyList<HighlightSpan>?)null), .. highlights], lines);
    }

    private static DiffLine PackageNote(PackageChange package) => new(DiffLineKind.Note, package switch
    {
        { From: null } => $"NuGet: + {package.Name} {package.To}",
        { To: null } => $"NuGet: − {package.Name} {package.From}",
        { IsMajor: true } => T($"NuGet: {package.Name} {package.From} → {package.To}  {Theme.SymbolWarning} мажорная версия: возможны ломающие изменения",
            $"NuGet: {package.Name} {package.From} → {package.To}  {Theme.SymbolWarning} major version: breaking changes are possible"),
        _ => $"NuGet: {package.Name} {package.From} → {package.To}",
    })
    {
        IsWarning = package.IsMajor,
    };

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
        var ago = RelativeTime.Ago(commit.Date, now);
        // Полный хеш отдельной строкой: его можно скопировать для git checkout / cherry-pick, и он не теснит дату.
        Add(new DiffLine(DiffLineKind.Header, commit.Hash));
        Add(new DiffLine(DiffLineKind.Header, $"{commit.AuthorName} {Theme.SymbolSeparator} {date} ({ago})"));
        foreach (var text in diff.Message.Split('\n'))
            Add(new DiffLine(DiffLineKind.Message, text.TrimEnd('\r')));

        AddFiles(diff.Lines, lines, highlights, T("В коммите нет изменений файлов", "The commit has no file changes"));
        return new PreparedDiff(lines, highlights.ToArray());
    }

    /// <summary>
    /// Подготовка PR: «#12 · ветка → main · автор · обновлён», состояние (CI, ревью, конфликты), заголовок и описание,
    /// проверки CI по одной, затем сводка файлов и diff — как у коммита.
    /// </summary>
    public static PreparedDiff PreparePullRequest(PullRequest pr, IReadOnlyList<DiffLine> diff, DateTimeOffset now)
    {
        var lines = new List<DiffLine>();
        var highlights = new List<IReadOnlyList<HighlightSpan>?>();
        void Add(DiffLine line)
        {
            lines.Add(line);
            highlights.Add(null);
        }

        var branches = pr.HeadRef is null ? "" : $" {Theme.SymbolSeparator} {pr.HeadRef} → {pr.BaseRef}";
        Add(new DiffLine(DiffLineKind.Header,
            $"{pr.Repo}#{pr.Number}{branches} {Theme.SymbolSeparator} {pr.Author} {Theme.SymbolSeparator} {RelativeTime.Ago(pr.UpdatedAt, now)}"));
        Add(new DiffLine(DiffLineKind.Header, PullRequestState(pr)));
        Add(new DiffLine(DiffLineKind.Message, pr.Title));

        // Комментарии <!-- … --> из шаблонов PR на GitHub не видны — здесь тоже.
        var body = Regex.Replace(pr.Body ?? "", "<!--.*?-->", "", RegexOptions.Singleline).Replace("\r", "");
        // После вырезанных комментариев остаются пачки пустых строк — оставляем по одной.
        body = Regex.Replace(body, "\n[ \t]*(\n[ \t]*)+\n", "\n\n").Trim();
        if (body.Length > 0)
        {
            Add(new DiffLine(DiffLineKind.Message, ""));
            foreach (var text in body.Split('\n'))
                Add(new DiffLine(DiffLineKind.Message, text.TrimEnd('\r')));
        }

        if (pr.CheckRuns.Count > 0)
        {
            Add(new DiffLine(DiffLineKind.Message, ""));
            foreach (var check in pr.CheckRuns.OrderBy(c => c.State switch { CheckState.Failure => 0, CheckState.Pending => 1, _ => 2 }))
                Add(new DiffLine(DiffLineKind.Header, $"{CheckSymbol(check.State)} {check.Name}"));
        }

        AddFiles(diff, lines, highlights, T("В PR нет изменений файлов", "The PR has no file changes"));
        return new PreparedDiff(lines, highlights.ToArray());
    }

    /// <summary>Issue: «репо#12 · автор · создан», метки и исполнители, заголовок, описание, затем комментарии с авторами.</summary>
    public static PreparedDiff PrepareIssue(Issue issue, DateTimeOffset now)
    {
        var lines = new List<DiffLine>();
        var created = issue.CreatedAt ?? issue.UpdatedAt;
        lines.Add(new DiffLine(DiffLineKind.Header,
            $"{issue.Repo}#{issue.Number} {Theme.SymbolSeparator} {issue.Author} {Theme.SymbolSeparator} {created.ToLocalTime():dd.MM.yyyy HH:mm} ({RelativeTime.Ago(created, now)})"));
        var meta = new List<string> { issue.State == "CLOSED" ? T("закрыт", "closed") : T("открыт", "open") };
        if (issue.Labels.Count > 0)
            meta.Add(T($"метки: {string.Join(", ", issue.Labels)}", $"labels: {string.Join(", ", issue.Labels)}"));
        if (issue.Assignees.Count > 0)
            meta.Add(T($"исполнители: {string.Join(", ", issue.Assignees)}", $"assignees: {string.Join(", ", issue.Assignees)}"));
        lines.Add(new DiffLine(DiffLineKind.Note, string.Join($" {Theme.SymbolSeparator} ", meta)));
        lines.Add(new DiffLine(DiffLineKind.Message, issue.Title));
        AddMarkdown(lines, issue.Body);

        foreach (var comment in issue.Comments)
        {
            lines.Add(new DiffLine(DiffLineKind.Message, ""));
            lines.Add(new DiffLine(DiffLineKind.File, $"{comment.Author} {Theme.SymbolSeparator} {comment.CreatedAt.ToLocalTime():dd.MM.yyyy HH:mm} ({RelativeTime.Ago(comment.CreatedAt, now)})"));
            AddMarkdown(lines, comment.Body);
        }

        if (issue.Comments.Count == 0)
        {
            lines.Add(new DiffLine(DiffLineKind.Message, ""));
            lines.Add(new DiffLine(DiffLineKind.Header, T("Комментариев нет. c — написать", "No comments. c writes one")));
        }

        return new PreparedDiff(lines, new IReadOnlyList<HighlightSpan>?[lines.Count]);
    }

    /// <summary>Текст Markdown строками сообщения: комментарии &lt;!-- --&gt; скрыты, пачки пустых строк — по одной.</summary>
    private static void AddMarkdown(List<DiffLine> lines, string? markdown)
    {
        var text = Regex.Replace(markdown ?? "", "<!--.*?-->", "", RegexOptions.Singleline).Replace("\r", "");
        text = Regex.Replace(text, "\n[ \t]*(\n[ \t]*)+\n", "\n\n").Trim();
        if (text.Length == 0)
            return;

        lines.Add(new DiffLine(DiffLineKind.Message, ""));
        foreach (var line in text.Split('\n'))
            lines.Add(new DiffLine(DiffLineKind.Message, line));
    }

    /// <summary>«✓ CI · ● нужно ревью · ✓ можно влить» — то, что решает, можно ли вливать PR.</summary>
    public static string PullRequestState(PullRequest pr)
    {
        var parts = new List<string>();
        if (pr.IsDraft)
            parts.Add(T("черновик", "draft"));
        if (pr.Checks != CheckState.None)
            parts.Add($"{CheckSymbol(pr.Checks)} CI");
        parts.Add(pr.Review switch
        {
            ReviewState.Approved => $"{Theme.SymbolSuccess} {T("одобрен", "approved")}",
            ReviewState.ChangesRequested => $"{Theme.SymbolError} {T("просят правки", "changes requested")}",
            ReviewState.Required => $"{Theme.SymbolCurrent} {T("ждёт ревью", "review required")}",
            _ => T("без ревью", "no review"),
        });
        if (pr.Mergeable == "CONFLICTING")
            parts.Add($"{Theme.SymbolError} {T("конфликт с базовой веткой", "conflicts with the base branch")}");
        return string.Join($" {Theme.SymbolSeparator} ", parts);
    }

    public static string CheckSymbol(CheckState state) => state switch
    {
        CheckState.Success => Theme.SymbolSuccess,
        CheckState.Failure => Theme.SymbolError,
        CheckState.Pending => Theme.SymbolCurrent,
        _ => " ",
    };

    /// <summary>Сводка файлов как git show --stat и diff каждого файла под заголовком с путём.</summary>
    private static void AddFiles(IReadOnlyList<DiffLine> diff, List<DiffLine> lines, List<IReadOnlyList<HighlightSpan>?> highlights, string empty)
    {
        void Add(DiffLine line)
        {
            lines.Add(line);
            highlights.Add(null);
        }

        var files = SplitByFile(diff);
        if (files.Count > 0)
        {
            // Сводка как в git show --stat: какие файлы изменены и насколько.
            Add(new DiffLine(DiffLineKind.Message, ""));
            int added = 0, removed = 0;
            foreach (var file in files)
            {
                var counts = file.Any(l => l.Kind == DiffLineKind.Binary)
                    ? ((int, int)?)null
                    : (file.Count(l => l.Kind == DiffLineKind.Added), file.Count(l => l.Kind == DiffLineKind.Removed));
                added += counts?.Item1 ?? 0;
                removed += counts?.Item2 ?? 0;
                Add(new DiffLine(DiffLineKind.Stat, FilePath(file)) { Counts = counts });
            }

            Add(new DiffLine(DiffLineKind.Header,
                $"{Plural(files.Count, "файл изменён", "файла изменено", "файлов изменено", "file changed", "files changed")}" +
                $" {Theme.SymbolSeparator} +{added} {Theme.SymbolSeparator} -{removed}"));
        }

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
            Add(new DiffLine(DiffLineKind.Header, empty));
        }

    }

    /// <summary>
    /// Вставляет сводку «что изменилось в коде» (типы и члены C#): в коммите — перед сводкой файлов, в diff файла — сверху.
    /// Если по файлам изменений кода нет — одна строка с пояснением.
    /// </summary>
    public static PreparedDiff WithCodeChanges(PreparedDiff diff, IReadOnlyList<(string Path, IReadOnlyList<CodeChange> Changes)> files)
    {
        var block = new List<DiffLine> { new(DiffLineKind.Header, T("Что изменилось в коде", "What changed in the code")) };
        if (files.Count == 0)
            block.Add(new DiffLine(DiffLineKind.Message, T("  Изменений в C#-коде нет (или только форматирование и комментарии)",
                "  No changes in C# code (or only formatting and comments)")));
        foreach (var (path, changes) in files)
        {
            if (files.Count > 1)
                block.Add(new DiffLine(DiffLineKind.Message, "  " + path));
            block.AddRange(changes.Select(c => new DiffLine(DiffLineKind.CodeChange, c.Name) { Change = c }));
        }

        block.Add(new DiffLine(DiffLineKind.Message, ""));

        var lines = diff.Lines.ToList();
        var at = lines.FindIndex(l => l.Kind == DiffLineKind.Stat);
        // Перед сводкой файлов стоит пустая строка-разделитель — блок идёт перед ней.
        at = at > 0 && lines[at - 1].Kind == DiffLineKind.Message && lines[at - 1].Text.Length == 0 ? at : Math.Max(0, at);
        var highlights = diff.Highlights.ToList();
        lines.InsertRange(at, block);
        highlights.InsertRange(at, Enumerable.Repeat<IReadOnlyList<HighlightSpan>?>(null, block.Count));
        return new PreparedDiff(lines, highlights.ToArray(), diff.Source);
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

    /// <summary>
    /// Перенос длинных строк кода и сообщения коммита на следующие экранные строки (по умолчанию включён); без него —
    /// обрезаются по ширине панели.
    /// </summary>
    public bool Wrap
    {
        get => _wrap;
        set
        {
            if (_wrap == value)
                return;

            // Та же строка diff остаётся вверху после переключения.
            var top = RowToLine(_scroll);
            _wrap = value;
            _rowsWidth = -1;
            _scroll = ClampScroll(FirstRow(top));
            SetNeedsDraw();
        }
    }

    /// <summary>Показывает diff. Без <paramref name="keepScroll"/> прокручивает в начало.</summary>
    public void SetDiff(PreparedDiff diff, bool keepScroll = false)
    {
        var topLine = RowToLine(_scroll);
        _lines = diff.Lines;
        _highlights = diff.Highlights;
        _source = diff.Source;
        _message = _lines.Count == 0 ? T("Нет изменений для показа", "No changes to show") : null;
        _rowsWidth = -1;

        var maxNumber = _lines.Select(l => Math.Max(l.OldNumber ?? 0, l.NewNumber ?? 0)).DefaultIfEmpty(0).Max();
        _numberWidth = Math.Max(3, maxNumber.ToString().Length);
        _scroll = keepScroll ? ClampScroll(FirstRow(topLine)) : 0;
        if (_staging)
        {
            // После stage строки сдвигаются: курсор встаёт на ближайшее изменение на прежнем месте.
            // Другой diff (переключение staged / не staged) — с первого изменения.
            _anchor = null;
            _cursor = NearestChange(keepScroll && _cursor >= 0 ? _cursor : RowToLine(_scroll));
            EnsureCursorVisible();
        }

        var stats = _lines.Where(l => l.Kind == DiffLineKind.Stat).ToList();
        _statPathWidth = stats.Select(l => l.Text.Length).DefaultIfEmpty(0).Max();
        _statMaxTotal = stats.Select(l => l.Counts is var (a, r) ? a + r : 0).DefaultIfEmpty(0).Max();
        // Шире числа только «Bin» у бинарного файла.
        _statCountWidth = Math.Max(_statMaxTotal.ToString().Length, stats.Any(l => l.Counts is null) ? 3 : 1);
        SetNeedsDraw();
    }

    /// <summary>
    /// Экранные строки: строка diff, номер куска и его символы. Пересчитываются при смене diff, ширины или переноса.
    /// </summary>
    private List<(int Line, int Part, int Start, int Length)> Rows
    {
        get
        {
            if (_rowsWidth == Viewport.Width)
                return _rows;

            _rowsWidth = Viewport.Width;
            _rows = new List<(int, int, int, int)>(_lines.Count);
            for (var i = 0; i < _lines.Count; i++)
            {
                var chunk = ChunkWidth(_lines[i]);
                if (!_wrap || chunk == 0 || Viewport.Width <= 0)
                {
                    _rows.Add((i, 0, 0, int.MaxValue));
                    continue;
                }

                var part = 0;
                foreach (var (start, length) in Breaks(ExpandTabs(_lines[i].Text), chunk))
                    _rows.Add((i, part++, start, length));
            }

            return _rows;
        }
    }

    /// <summary>
    /// Куски строки по ширине <paramref name="chunk"/>: перенос по последнему пробелу, если он не дальше половины куска,
    /// иначе (длинное слово) — ровно по ширине. Пустая строка — один пустой кусок.
    /// </summary>
    public static IEnumerable<(int Start, int Length)> Breaks(string text, int chunk)
    {
        var start = 0;
        do
        {
            var end = Math.Min(text.Length, start + chunk);
            if (end < text.Length)
            {
                var space = text.LastIndexOf(' ', end - 1, end - start);
                if (space >= start + chunk / 2)
                    end = space + 1;
            }

            yield return (start, end - start);
            start = end;
        }
        while (start < text.Length);
    }

    /// <summary>Ширина колонок номеров и знака перед кодом: «  12  13 + ».</summary>
    private int CodePrefix => _numberWidth * 2 + 4;

    /// <summary>Сколько символов строки помещается в одну экранную строку; 0 — строка не переносится.</summary>
    private int ChunkWidth(DiffLine line) => line.Kind switch
    {
        DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed => Math.Max(10, Viewport.Width - CodePrefix),
        // Служебные строки тоже переносятся: длинный «@@ … @@ контекст», строка автора, заметка NuGet.
        DiffLineKind.Message or DiffLineKind.Header or DiffLineKind.Hunk or DiffLineKind.Note or DiffLineKind.Binary => Math.Max(10, Viewport.Width - 1),
        _ => 0,
    };

    /// <summary>Сколько экранных строк занимает строка diff.</summary>
    private int Parts(int index)
    {
        var first = FirstRow(index);
        var rows = Rows;
        var count = 0;
        while (first + count < rows.Count && rows[first + count].Line == index)
            count++;
        return Math.Max(1, count);
    }

    /// <summary>Первая экранная строка строки diff <paramref name="line"/>.</summary>
    private int FirstRow(int line)
    {
        var rows = Rows;
        int low = 0, high = rows.Count - 1;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (rows[middle].Line < line)
                low = middle + 1;
            else
                high = middle;
        }

        return Math.Max(0, low);
    }

    /// <summary>Строка diff, которой принадлежит экранная строка <paramref name="row"/>.</summary>
    private int RowToLine(int row) => Rows.Count == 0 ? 0 : Rows[Math.Clamp(row, 0, Rows.Count - 1)].Line;

    private int ClampScroll(int scroll) => Math.Clamp(scroll, 0, Math.Max(0, Rows.Count - Viewport.Height));

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
        _cursor = NearestChange(RowToLine(_scroll));
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
        var top = FirstRow(_cursor > 0 && _lines[_cursor - 1].Kind == DiffLineKind.Hunk ? _cursor - 1 : _cursor);
        var bottom = FirstRow(_cursor) + Parts(_cursor) - 1;
        if (top < _scroll)
            _scroll = top;
        else if (bottom >= _scroll + Viewport.Height)
            _scroll = Math.Max(top, bottom - Viewport.Height + 1);
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
        _rowsWidth = -1;
        SetNeedsDraw();
    }

    /// <summary>Прокрутка на <paramref name="delta"/> строк; отрицательное значение — вверх.</summary>
    public void ScrollBy(int delta)
    {
        var scroll = ClampScroll(_scroll + delta);
        if (scroll == _scroll)
            return;

        _scroll = scroll;
        SetNeedsDraw();
    }

    /// <summary>Колесо мыши листает diff по <see cref="ListPanel{T}.WheelStep"/> строки, и в режиме построчного stage тоже.</summary>
    protected override bool OnMouseEvent(Mouse mouse)
    {
        if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            ScrollBy(ListPanel<object>.WheelStep);
        else if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            ScrollBy(-ListPanel<object>.WheelStep);
        else
            return base.OnMouseEvent(mouse);
        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_message is not null)
        {
            this.DrawSegments(1, 0, [new Segment(_message, Theme.Muted)], Viewport.Width - 1);
            DrawMascot(usedRows: 1);
            return true;
        }

        var rows = Rows;
        _scroll = ClampScroll(_scroll);
        for (var row = 0; row < Viewport.Height && _scroll + row < rows.Count; row++)
        {
            var (line, part, start, length) = rows[_scroll + row];
            DrawLine(row, line, part, start, length);
        }

        DrawMascot(usedRows: Math.Min(Viewport.Height, rows.Count - _scroll));
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

    /// <param name="part">Кусок перенесённой строки: 0 — начало (с номерами и знаком), дальше — продолжения.</param>
    /// <param name="start">Первый символ куска в строке (с развёрнутыми табуляциями).</param>
    /// <param name="length">Длина куска; без переноса — вся строка.</param>
    private void DrawLine(int row, int index, int part = 0, int start = 0, int length = int.MaxValue)
    {
        var line = _lines[index];
        var highlight = _highlights[index];
        var width = Viewport.Width;
        if (_wrap && length != int.MaxValue && line.Kind is DiffLineKind.Message or DiffLineKind.Header or DiffLineKind.Hunk
                or DiffLineKind.Note or DiffLineKind.Binary)
        {
            var subjectLine = index == 0 || _lines[index - 1].Kind != DiffLineKind.Message;
            var (textColor, style) = line.Kind switch
            {
                DiffLineKind.Message => (Theme.Fg, subjectLine ? TextStyle.Bold : TextStyle.None),
                DiffLineKind.Hunk => (Theme.Info, TextStyle.None),
                DiffLineKind.Note => (line.IsWarning ? Theme.Warning : Theme.Info, TextStyle.Bold),
                _ => (Theme.Muted, TextStyle.Italic),
            };
            this.DrawSegments(1, row, Slice([new Segment(ExpandTabs(line.Text), textColor, style)], start, length), width - 1);
            return;
        }

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
            case DiffLineKind.Stat:
                this.DrawSegments(0, row, StatSegments(line, width), width);
                return;
            case DiffLineKind.Note:
                this.DrawSegments(1, row, [new Segment(line.Text, line.IsWarning ? Theme.Warning : Theme.Info, TextStyle.Bold)], width - 1);
                return;
            case DiffLineKind.CodeChange when line.Change is { } change:
                var (sign, signColor) = change.Kind switch
                {
                    CodeChangeKind.Added => ("+", Theme.Success),
                    CodeChangeKind.Removed => ("−", Theme.Error),
                    _ => ("~", Theme.Warning),
                };
                this.DrawSegments(1, row,
                [
                    new Segment($"  {sign} ", signColor, TextStyle.Bold),
                    new Segment(change.What.PadRight(12), Theme.Muted),
                    new Segment(change.Name, change.Kind == CodeChangeKind.Removed ? Theme.Muted : Theme.Fg),
                ], width - 1);
                return;
            case DiffLineKind.NoNewline:
                this.DrawSegments(0, row, [new Segment(new string(' ', _numberWidth * 2 + 3) + T("⏎ нет перевода строки в конце файла", "⏎ no newline at end of file"), Theme.Dim, TextStyle.Italic)], width);
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

        List<Segment> segments;
        if (part == 0)
        {
            var old = line.OldNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
            var @new = line.NewNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
            segments = [new($"{old} {@new} ", Theme.Dim), new(marker + " ", color, TextStyle.Bold)];
        }
        else
        {
            // Продолжение перенесённой строки: номера пустые, «↪» вместо знака — код остаётся в своей колонке.
            segments = [new(new string(' ', _numberWidth * 2 + 2), Theme.Dim), new("↪ ", Theme.Dim)];
        }

        var code = highlight is null
            ? [new Segment(ExpandTabs(line.Text), line.Kind == DiffLineKind.Context ? Theme.Fg : color)]
            : SyntaxSegments(line.Text, highlight).ToList();
        if (_wrap && length != int.MaxValue)
            code = Slice(code, start, length);

        segments.AddRange(code);
        this.DrawSegments(0, row, segments, width, background);
    }

    /// <summary>Символы [<paramref name="start"/>, start + <paramref name="length"/>) из цветных фрагментов — кусок для переноса.</summary>
    private static List<Segment> Slice(IReadOnlyList<Segment> segments, int start, int length)
    {
        var result = new List<Segment>();
        var position = 0;
        foreach (var segment in segments)
        {
            var from = Math.Max(start, position);
            var to = (int)Math.Min((long)start + length, position + segment.Text.Length);
            if (to > from)
                result.Add(segment with { Text = segment.Text[(from - position)..(to - position)] });
            position += segment.Text.Length;
            if (position >= (long)start + length)
                break;
        }

        return result;
    }

    /// <summary>
    /// « src/Cli.cs     | 12 ++++++----»: путь, число изменённых строк и полоска «+» и «-» как в git --stat.
    /// Длинный путь сокращается слева, полоска масштабируется под ширину панели.
    /// </summary>
    private List<Segment> StatSegments(DiffLine line, int width)
    {
        var total = line.Counts is var (a, r) ? a + r : 0;
        var countText = line.Counts is null ? "Bin" : total.ToString();
        var countWidth = _statCountWidth;
        var pathWidth = Math.Min(_statPathWidth, Math.Max(10, width / 2));
        var path = line.Text.Length > pathWidth ? Theme.SymbolEllipsis + line.Text[^(pathWidth - 1)..] : line.Text.PadRight(pathWidth);

        var segments = new List<Segment>
        {
            new(" " + path, Theme.Fg),
            new(" | ", Theme.Dim),
            new(countText.PadLeft(countWidth) + " ", Theme.Muted),
        };
        if (line.Counts is not var (added, removed) || total == 0)
            return segments;

        var barWidth = Math.Max(1, width - 1 - pathWidth - 3 - countWidth - 2);
        if (_statMaxTotal > barWidth)
        {
            // Как git: у файла с изменениями хотя бы один знак каждого вида.
            var plus = added == 0 ? 0 : Math.Max(1, added * barWidth / _statMaxTotal);
            var minus = removed == 0 ? 0 : Math.Max(1, removed * barWidth / _statMaxTotal);
            (added, removed) = (plus, minus);
        }

        segments.Add(new Segment(new string('+', added), Theme.Success));
        segments.Add(new Segment(new string('-', removed), Theme.Error));
        return segments;
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
            return line with { Text = T("Бинарный файл: содержимое не показывается", "Binary file: content is not shown") };
        if (line.Kind != DiffLineKind.Header)
            return line;

        var text = line.Text;
        if (text.StartsWith("new file mode"))
            return line with { Text = T("Новый файл", "New file") };
        if (text.StartsWith("deleted file mode"))
            return line with { Text = T("Файл удалён", "File deleted") };
        if (text.StartsWith("rename from "))
            return line with { Text = T("Переименован из ", "Renamed from ") + text["rename from ".Length..] };
        if (text.StartsWith("old mode") || text.StartsWith("new mode"))
            return line with { Text = T("Права доступа: ", "Mode: ") + text };
        return null;
    }
}
