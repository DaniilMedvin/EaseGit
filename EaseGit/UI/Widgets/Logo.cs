using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Attribute = Terminal.Gui.Drawing.Attribute;

namespace EaseGit.UI.Widgets;

/// <summary>Надпись EaseGit шрифтом ANSI Shadow с вертикальным градиентом от синего к зелёному.</summary>
public sealed class Logo : View
{
    private static readonly string[] Lines =
    [
        "███████╗ █████╗ ███████╗███████╗ ██████╗ ██╗████████╗",
        "██╔════╝██╔══██╗██╔════╝██╔════╝██╔════╝ ██║╚══██╔══╝",
        "█████╗  ███████║███████╗█████╗  ██║  ███╗██║   ██║   ",
        "██╔══╝  ██╔══██║╚════██║██╔══╝  ██║   ██║██║   ██║   ",
        "███████╗██║  ██║███████║███████╗╚██████╔╝██║   ██║   ",
        "╚══════╝╚═╝  ╚═╝╚══════╝╚══════╝ ╚═════╝ ╚═╝   ╚═╝   ",
    ];

    public Logo()
    {
        Width = Lines.Max(l => l.Length);
        Height = Lines.Length;
        CanFocus = false;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        for (var row = 0; row < Lines.Length; row++)
        {
            var color = Theme.Blend(Theme.Accent, Theme.Green, row / (Lines.Length - 1.0));
            SetAttribute(new Attribute(color, Color.None, TextStyle.Bold));
            AddStr(0, row, Lines[row]);
        }

        return true;
    }
}
