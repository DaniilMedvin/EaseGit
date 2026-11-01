using LazyGit.Git;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace LazyGit.UI.Panels;

/// <summary>
/// Command Log: git-команды, выполненные по действиям пользователя, новые внизу.
/// Строка: время, ✓ / ✗ / ⊘ (отменена), команда и длительность справа; у ошибки ниже — первая строка вывода.
/// </summary>
public sealed class CommandLogPanel : View
{
    private IReadOnlyList<CommandLogEntry> _entries = [];

    public CommandLogPanel()
    {
        CanFocus = false;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    public void SetEntries(IReadOnlyList<CommandLogEntry> entries)
    {
        _entries = entries;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        if (_entries.Count == 0)
        {
            this.DrawSegments(1, 0, [new Segment(T("Здесь появятся команды, которые выполняет LGit: stage, commit, push…", "Commands run by LGit will appear here: stage, commit, push…"), Theme.Muted)],
                Viewport.Width - 1);
            return true;
        }

        // Заполняем снизу вверх, чтобы последняя команда всегда была видна.
        var lines = _entries.SelectMany(Lines).ToList();
        var first = Math.Max(0, lines.Count - Viewport.Height);
        for (var row = 0; row + first < lines.Count && row < Viewport.Height; row++)
            DrawLine(row, lines[first + row]);

        return true;
    }

    private void DrawLine(int row, (CommandLogEntry Entry, bool IsDetail) line)
    {
        var entry = line.Entry;
        if (line.IsDetail)
        {
            this.DrawSegments(1, row, [new Segment("           " + GitErrors.FirstLine(entry.Output), Theme.Error)], Viewport.Width - 1);
            return;
        }

        var (symbol, color) = entry switch
        {
            { Cancelled: true } => ("⊘", Theme.Muted),
            { Success: true } => (Theme.SymbolSuccess, Theme.Success),
            _ => (Theme.SymbolError, Theme.Error),
        };
        var duration = entry.Cancelled ? T("отменена", "cancelled") : FormatDuration(entry.Duration);
        var durationX = Viewport.Width - duration.Length - 1;

        this.DrawSegments(1, row,
        [
            new Segment(entry.Time.ToLocalTime().ToString("HH:mm:ss") + " ", Theme.Dim),
            new Segment(symbol + " ", color),
            new Segment(CommandText.Format(entry.Args, entry.Tool), Theme.Fg),
        ], Math.Max(0, durationX - 2));
        this.DrawSegments(durationX, row, [new Segment(duration, Theme.Dim)], duration.Length);
    }

    /// <summary>Строки записи: сама команда и, если она упала, первая строка вывода.</summary>
    private static IEnumerable<(CommandLogEntry, bool)> Lines(CommandLogEntry entry)
    {
        yield return (entry, false);
        if (!entry.Success && !entry.Cancelled && entry.Output.Length > 0)
            yield return (entry, true);
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 1 ? T($"{duration.TotalMilliseconds:0} мс", $"{duration.TotalMilliseconds:0} ms") : T($"{duration.TotalSeconds:0.0} с", $"{duration.TotalSeconds:0.0} s");
}
