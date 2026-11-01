using LazyGit.Config;
using LazyGit.UI.Widgets;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace LazyGit.UI.Dialogs;

/// <summary>
/// Настройки. Пока одна: маскот в углу панели diff, справа — его предпросмотр.
/// ↑↓ / j k — выбор, enter — сохранить, esc — отмена. Result — выбранный маскот или null.
/// </summary>
public sealed class SettingsDialog : Runnable<Mascot?>
{
    private const int DialogWidth = 46;

    public SettingsDialog(Mascot current)
    {
        X = Pos.Center();
        Y = Pos.Center();
        Width = DialogWidth;
        Height = MascotArt.Rows + 5;
        SetScheme(Theme.Base);

        var box = new View
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = MascotArt.Rows + 4,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
        };
        box.SetScheme(Theme.Text(Theme.Accent));
        box.Padding!.Thickness = new Thickness(1, 0, 1, 0);

        var caption = new Label { Text = T("Маскот в углу diff", "Mascot in the diff corner"), X = 0, Y = 0 };
        caption.SetScheme(Theme.Text(Theme.Fg, TextStyle.Bold));

        var picker = new MascotPicker(current) { X = 0, Y = 2, Width = Dim.Fill(), Height = MascotArt.Rows };
        picker.Accepted += (_, mascot) =>
        {
            Result = mascot;
            RequestStop();
        };

        var hints = new HintBar { X = 1, Y = Pos.Bottom(box) };
        hints.SetHints([new Hint("↑↓", T("выбор", "choose")), new Hint("enter", T("сохранить", "save")), new Hint("esc", T("отмена", "cancel"))]);

        box.Add(caption, picker);
        Add(box, BoxTitle.For(box, T("Настройки", "Settings"), Theme.Accent), hints);
        picker.SetFocus();
    }

    /// <summary>Список вариантов слева и предпросмотр выбранного справа.</summary>
    private sealed class MascotPicker : View
    {
        private static readonly Mascot[] Options = Enum.GetValues<Mascot>();

        private int _selected;

        public MascotPicker(Mascot current)
        {
            CanFocus = true;
            _selected = Array.IndexOf(Options, current);
        }

        public event EventHandler<Mascot>? Accepted;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.CursorDown || key == Key.J)
                return Move(1);
            if (key == Key.CursorUp || key == Key.K)
                return Move(-1);
            if (key == Key.Enter)
            {
                Accepted?.Invoke(this, Options[_selected]);
                return true;
            }

            return base.OnKeyDown(key);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            for (var i = 0; i < Options.Length; i++)
            {
                var selected = i == _selected;
                if (selected)
                {
                    SetAttribute(new Attribute(Theme.Fg, Theme.SelectionBg));
                    AddStr(0, i, new string(' ', 14));
                }

                this.DrawSegments(0, i,
                [
                    new Segment(selected ? Theme.SymbolSelection + " " : "  ", Theme.Accent, TextStyle.Bold),
                    new Segment(MascotArt.Name(Options[i]), selected ? Theme.Fg : Theme.Muted),
                ], 14, selected ? Theme.SelectionBg : null);
            }

            var previewX = Viewport.Width - MascotArt.Width - 2;
            if (Options[_selected] == Mascot.None)
                this.DrawSegments(previewX, MascotArt.Rows / 2 - 1, [new Segment(T("без маскота", "no mascot"), Theme.Dim)], MascotArt.Width + 2);
            else
                MascotArt.Draw(this, previewX, 0, Options[_selected]);
            return true;
        }

        private bool Move(int delta)
        {
            _selected = (_selected + delta + Options.Length) % Options.Length;
            SetNeedsDraw();
            return true;
        }
    }
}
