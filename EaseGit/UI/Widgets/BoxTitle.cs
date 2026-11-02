using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace EaseGit.UI.Widgets;

public static class BoxTitle
{
    /// <summary>
    /// Заголовок поверх верхней рамки в формате «╭─ Заголовок ───╮»
    /// (встроенный Title рисуется как «┤Заголовок├»).
    /// Добавляется в тот же SuperView, что и box, после него.
    /// </summary>
    public static Label For(View box, string title, Terminal.Gui.Drawing.Color color)
    {
        var label = new Label
        {
            Text = $" {title} ",
            X = Pos.Left(box) + 2,
            Y = Pos.Top(box),
        };
        label.SetScheme(Theme.Text(color));
        return label;
    }

    /// <summary>
    /// Заголовок панели, которая получает фокус: рамка и заголовок цвета accent в фокусе, dim — без него.
    /// </summary>
    public static Label ForFocusable(View box, string title)
    {
        var label = For(box, title, Theme.Dim);
        box.SetScheme(Theme.Text(Theme.Dim));
        box.HasFocusChanged += (_, _) =>
        {
            var color = box.HasFocus ? Theme.Accent : Theme.Dim;
            box.SetScheme(Theme.Text(color));
            label.SetScheme(Theme.Text(color));
        };
        return label;
    }
}
