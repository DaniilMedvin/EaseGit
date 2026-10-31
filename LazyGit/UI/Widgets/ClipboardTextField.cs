using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace LazyGit.UI.Widgets;

/// <summary>
/// Однострочное поле ввода с системным буфером обмена:
/// ctrl+c — копировать (без выделения — весь текст), ctrl+x — вырезать, ctrl+v — вставить, ctrl+a — выделить всё.
/// Переводы строк во вставляемом тексте заменяются пробелами.
/// </summary>
public sealed class ClipboardTextField : TextField
{
    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.V.WithCtrl)
        {
            Paste(SystemClipboard.GetText());
            return true;
        }

        if (key == Key.C.WithCtrl)
        {
            SystemClipboard.SetText(SelectedLength > 0 ? SelectedText ?? "" : Text);
            return true;
        }

        if (key == Key.X.WithCtrl)
        {
            if (SelectedLength > 0)
            {
                SystemClipboard.SetText(SelectedText ?? "");
                ReplaceSelection("");
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

    private void Paste(string text)
    {
        var singleLine = text.ReplaceLineEndings(" ").TrimEnd();
        if (singleLine.Length > 0)
            ReplaceSelection(singleLine);
    }

    /// <summary>Заменяет выделение (или вставляет в позицию курсора) и ставит курсор после вставки.</summary>
    private void ReplaceSelection(string replacement)
    {
        var text = Text;
        int start, length;
        if (SelectedLength > 0)
        {
            start = Math.Min(SelectedStart, InsertionPoint);
            length = SelectedLength;
        }
        else
        {
            start = InsertionPoint;
            length = 0;
        }

        start = Math.Clamp(start, 0, text.Length);
        length = Math.Clamp(length, 0, text.Length - start);

        ClearAllSelection();
        Text = text[..start] + replacement + text[(start + length)..];
        InsertionPoint = start + replacement.Length;
    }
}
