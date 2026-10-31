using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Многострочное поле ввода с системным буфером обмена:
/// ctrl+c — копировать (без выделения — весь текст), ctrl+x — вырезать, ctrl+v — вставить, ctrl+a — выделить всё.
/// </summary>
// TextView устарел в пользу отдельного пакета tui-cs/Editor, но для тела коммита его возможностей достаточно.
#pragma warning disable CS0618
public sealed class ClipboardTextView : TextView
#pragma warning restore CS0618
{
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.V.WithCtrl)
        {
            var text = SystemClipboard.GetText().ReplaceLineEndings("\n");
            if (text.Length > 0)
                InsertText(text);
            return true;
        }

        if (key == Key.C.WithCtrl)
        {
            SystemClipboard.SetText(SelectedLength > 0 ? SelectedText : Text);
            return true;
        }

        if (key == Key.X.WithCtrl)
        {
            if (SelectedLength > 0)
            {
                SystemClipboard.SetText(SelectedText);
                // Cut удаляет выделение; буфер Terminal.Gui здесь не используется.
                Cut();
            }

            return true;
        }

        if (key == Key.A.WithCtrl)
        {
            SelectAll();
            return true;
        }

        return base.OnKeyDown(key);
    }
}
