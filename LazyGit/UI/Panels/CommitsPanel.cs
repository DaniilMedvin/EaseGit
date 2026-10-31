using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>
/// Лог текущей ветки, новые коммиты сверху: короткий хеш, заголовок и давность справа.
/// Навигация: ↑↓ / j k, Home / End, PageUp / PageDown.
/// </summary>
public sealed class CommitsPanel : View
{
    private IReadOnlyList<Commit> _commits = [];
    private int _selected = -1;
    private int _scroll;

    public CommitsPanel()
    {
        CanFocus = true;
        BorderStyle = LineStyle.Rounded;
    }

    /// <summary>Выбранный коммит или null, если коммитов нет.</summary>
    public Commit? SelectedCommit => _selected >= 0 ? _commits[_selected] : null;

    public event EventHandler? SelectionChanged;

    /// <summary>Обновляет список, по возможности сохраняя выбранный коммит.</summary>
    public void SetCommits(IReadOnlyList<Commit> commits)
    {
        var previous = SelectedCommit;
        _commits = commits;

        var same = previous is null ? -1 : IndexOf(previous.Hash);
        _selected = same >= 0 ? same : commits.Count > 0 ? 0 : -1;

        EnsureVisible();
        SetNeedsDraw();
        if (SelectedCommit?.Hash != previous?.Hash)
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
            return Select(_commits.Count - 1);

        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_commits.Count == 0)
        {
            const string text = "Коммитов пока нет";
            this.DrawSegments(Math.Max(0, (Viewport.Width - text.Length) / 2), Viewport.Height / 2,
                [new Segment(text, Theme.Muted)], Viewport.Width);
            return true;
        }

        var now = DateTimeOffset.Now;
        for (var line = 0; line < Viewport.Height; line++)
        {
            var index = _scroll + line;
            if (index >= _commits.Count)
                break;

            DrawCommit(line, _commits[index], index == _selected, now);
        }

        return true;
    }

    private void DrawCommit(int line, Commit commit, bool selected, DateTimeOffset now)
    {
        var background = selected ? Theme.SelectionBg : (Color?)null;
        if (selected)
        {
            SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
            AddStr(0, line, new string(' ', Viewport.Width));
        }

        // Давность прижата к правому краю; на узкой панели её место отдаётся заголовку.
        var age = RelativeTime.Format(commit.Date, now);
        var showAge = Viewport.Width >= 30;
        var textWidth = Viewport.Width - 1 - (showAge ? age.Length + 2 : 1);

        this.DrawSegments(1, line,
        [
            new Segment(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold),
            new Segment(commit.ShortHash, Theme.Muted),
            new Segment(" " + commit.Subject, Theme.Fg),
        ], textWidth, background);

        if (showAge)
            this.DrawSegments(Viewport.Width - age.Length - 1, line, [new Segment(age, Theme.Dim)], age.Length, background);
    }

    private bool Select(int index)
    {
        if (_commits.Count == 0)
            return true;

        index = Math.Clamp(index, 0, _commits.Count - 1);
        if (index == _selected)
            return true;

        _selected = index;
        EnsureVisible();
        SetNeedsDraw();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private int IndexOf(string hash)
    {
        for (var i = 0; i < _commits.Count; i++)
        {
            if (_commits[i].Hash == hash)
                return i;
        }

        return -1;
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
