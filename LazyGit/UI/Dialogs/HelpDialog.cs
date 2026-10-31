using LazyGit.Models;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Dialogs;

/// <summary>Справка главного экрана: плашки, разделы, шапка и клавиши. Закрывается по ? или Esc.</summary>
public sealed class HelpDialog : Runnable<bool?>
{
    private const int DialogWidth = 78;

    public HelpDialog()
    {
        var content = new HelpContent { X = 0, Y = 0, Width = Dim.Fill() };
        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = content.LineCount + 2,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);
        content.Height = content.LineCount;
        box.Add(content);

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("esc / ?", "закрыть")]);

        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = content.LineCount + 3;
        SetScheme(Theme.Base);
        Add(box, BoxTitle.For(box, "Справка", Theme.Accent), hints);
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key.AsRune.Value == '?' || key == Key.F1)
        {
            RequestStop();
            return true;
        }

        return base.OnKeyDown(key);
    }

    /// <summary>Содержимое справки: строки из цветных фрагментов.</summary>
    private sealed class HelpContent : View
    {
        private const int KeyColumn = 10;
        private const int SecondColumn = 36;

        private readonly List<Segment[]> _lines = BuildLines();

        public HelpContent()
        {
            CanFocus = false;
        }

        public int LineCount => _lines.Count;

        protected override bool OnDrawingContent(DrawContext? context)
        {
            for (var i = 0; i < _lines.Count && i < Viewport.Height; i++)
                this.DrawSegments(0, i, _lines[i], Viewport.Width);
            return true;
        }

        private static List<Segment[]> BuildLines()
        {
            var lines = new List<Segment[]>();

            lines.Add(Heading("Плашки файлов"));
            lines.Add([.. Badge(ChangeKind.Modified, "изменён"), .. Badge(ChangeKind.Added, "добавлен"), .. Badge(ChangeKind.Deleted, "удалён")]);
            lines.Add([.. Badge(ChangeKind.Renamed, "переименован"), .. Badge(ChangeKind.Untracked, "новый файл"), .. Badge(ChangeKind.Unmerged, "конфликт")]);
            lines.Add([.. Badge(ChangeKind.Copied, "скопирован"), .. Badge(ChangeKind.TypeChanged, "сменился тип файла")]);
            lines.Add([]);

            lines.Add(Heading("Разделы"));
            lines.Add(Section("Staged", Theme.Success, "войдёт в следующий коммит"));
            lines.Add(Section("Изменения", Theme.Warning, "изменено, но ещё не добавлено в индекс"));
            lines.Add(Section("Новые файлы", Theme.Cyan, "git их ещё не отслеживает"));
            lines.Add(Section("Конфликты", Theme.Error, "нужно решить вручную и добавить в индекс"));
            lines.Add([]);

            lines.Add(Heading("Шапка"));
            lines.Add([Segment.Pill($"{Theme.SymbolBranch} main", Theme.Info, TextStyle.Bold), new(" текущая ветка", Theme.Muted)]);
            lines.Add([new($"{Theme.SymbolAhead}2", Theme.Success), new(" ещё не отправлено (push)   ", Theme.Muted),
                new($"{Theme.SymbolBehind}1", Theme.Warning), new(" новое на сервере (pull)", Theme.Muted)]);
            lines.Add([Segment.Pill($"{Theme.SymbolCurrent} login", Theme.Success), new(" вход в GitHub выполнен   ", Theme.Muted),
                Segment.Pill($"{Theme.SymbolEmpty} Не авторизован", Theme.Warning), new(" ctrl+g — войти", Theme.Muted)]);
            lines.Add([]);

            lines.Add(Heading("Клавиши"));
            lines.Add(Keys("space", "stage / unstage файла", "c", "коммит"));
            lines.Add(Keys("enter", "свернуть папку", "← →", "свернуть / развернуть"));
            lines.Add(Keys("a", "stage / unstage всех", "ctrl+d/u", "листать diff"));
            lines.Add(Keys("↑↓  j k", "выбор строки", "Home/End", "первая / последняя"));
            lines.Add(Keys("1  3", "файлы / коммиты", "tab", "следующая панель"));
            lines.Add(Keys("r", "обновить", "ctrl+g", "войти в GitHub"));
            lines.Add(Keys("esc", "назад к выбору репо", "q", "выход"));
            lines.Add(Keys("?  F1", "эта справка", "", ""));
            return lines;
        }

        private static Segment[] Heading(string text) => [new(text, Theme.Accent, TextStyle.Bold)];

        /// <summary>Плашка с подписью в колонке фиксированной ширины (три колонки в строке).</summary>
        private static Segment[] Badge(ChangeKind kind, string description)
        {
            const int descriptionWidth = 18;
            var (letter, color) = Theme.ForChange(kind);
            return [new("  ", Theme.Fg), Segment.Badge(letter, color), new(" " + description.PadRight(descriptionWidth), Theme.Muted)];
        }

        private static Segment[] Section(string title, Color color, string description) =>
        [
            new("  ", Theme.Fg),
            Segment.Pill(title, color, TextStyle.Bold),
            new(new string(' ', Math.Max(1, 16 - title.Length)) + description, Theme.Muted),
        ];

        private static Segment[] Keys(string key1, string description1, string key2, string description2)
        {
            var first = "  " + key1.PadRight(KeyColumn) + description1;
            return
            [
                new("  " + key1.PadRight(KeyColumn), Theme.Fg, TextStyle.Bold),
                new(description1.PadRight(Math.Max(0, SecondColumn - first.Length + description1.Length)), Theme.Muted),
                new(key2.PadRight(KeyColumn), Theme.Fg, TextStyle.Bold),
                new(description2, Theme.Muted),
            ];
        }
    }
}
