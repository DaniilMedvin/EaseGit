using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Panels;

/// <summary>
/// Diff выбранного файла: номера строк, «+» и «-» на цветном фоне, заголовки фрагментов.
/// Служебные строки git (diff --git, index, ---, +++) скрыты, важные показываются коротко по-русски.
/// </summary>
public sealed class DiffPanel : View
{
    private const int TabWidth = 4;

    private IReadOnlyList<DiffLine> _lines = [];
    private string? _message;
    private int _scroll;
    private int _numberWidth = 3;

    public DiffPanel()
    {
        CanFocus = false;
        BorderStyle = LineStyle.Rounded;
        SetScheme(Theme.Text(Theme.Dim));
    }

    /// <summary>Показывает diff и прокручивает в начало.</summary>
    public void SetDiff(IReadOnlyList<DiffLine> lines)
    {
        _lines = lines.Select(Simplify).OfType<DiffLine>().ToList();
        _message = _lines.Count == 0 ? "Нет изменений для показа" : null;
        _scroll = 0;
        var maxNumber = _lines.Select(l => Math.Max(l.OldNumber ?? 0, l.NewNumber ?? 0)).DefaultIfEmpty(0).Max();
        _numberWidth = Math.Max(3, maxNumber.ToString().Length);
        SetNeedsDraw();
    }

    /// <summary>Показывает сообщение вместо diff (загрузка, ошибка, нет выбранного файла).</summary>
    public void SetMessage(string message)
    {
        _lines = [];
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
            return true;
        }

        for (var row = 0; row < Viewport.Height && _scroll + row < _lines.Count; row++)
            DrawLine(row, _lines[_scroll + row]);

        return true;
    }

    private void DrawLine(int row, DiffLine line)
    {
        var width = Viewport.Width;
        switch (line.Kind)
        {
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

        if (background is { } bg)
        {
            SetAttribute(new Attribute(Theme.Fg, bg));
            AddStr(0, row, new string(' ', width));
        }

        var old = line.OldNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
        var @new = line.NewNumber?.ToString().PadLeft(_numberWidth) ?? new string(' ', _numberWidth);
        this.DrawSegments(0, row,
        [
            new Segment($"{old} {@new} ", Theme.Dim),
            new Segment(marker + " ", color, TextStyle.Bold),
            new Segment(line.Text.Replace("\t", new string(' ', TabWidth)), line.Kind == DiffLineKind.Context ? Theme.Fg : color),
        ], width, background);
    }

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
