using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace LazyGit.UI.Widgets;

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
}
