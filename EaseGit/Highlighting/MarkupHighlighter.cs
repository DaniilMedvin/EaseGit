namespace EaseGit.Highlighting;

/// <summary>
/// Подсветка XML-разметки (.csproj, .props, .xaml, .resx, .config…) и Razor (.razor, .cshtml) в цветах тёмной темы
/// Visual Studio: «&lt; &gt; /» — пунктуация, имя тега — ключевое слово, атрибут — переменная, значение — строка,
/// комментарий — комментарий. В Razor ещё «@code», «@if»… — управляющие слова, «@выражение» — переменная.
/// Простой лексер без разбора дерева: справляется и с обрывками разметки, какие бывают в diff.
/// </summary>
public static class MarkupHighlighter
{
    private static readonly HashSet<string> RazorDirectives = new(StringComparer.Ordinal)
    {
        "code", "functions", "page", "using", "inject", "inherits", "implements", "layout", "model", "namespace",
        "attribute", "typeparam", "rendermode", "if", "else", "foreach", "for", "while", "do", "switch", "try", "catch",
        "finally", "lock", "section", "await",
    };

    public static IReadOnlyList<HighlightSpan> Highlight(string text, bool razor)
    {
        var spans = new List<HighlightSpan>();
        var i = 0;
        while (i < text.Length)
        {
            if (Starts(text, i, "<!--"))
            {
                var end = IndexAfter(text, i + 4, "-->");
                spans.Add(new HighlightSpan(i, end - i, TokenClass.Comment));
                i = end;
            }
            else if (razor && Starts(text, i, "@*"))
            {
                var end = IndexAfter(text, i + 2, "*@");
                spans.Add(new HighlightSpan(i, end - i, TokenClass.Comment));
                i = end;
            }
            else if (Starts(text, i, "<![CDATA["))
            {
                var end = IndexAfter(text, i + 9, "]]>");
                spans.Add(new HighlightSpan(i, end - i, TokenClass.String));
                i = end;
            }
            else if (text[i] == '<' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] is '/' or '?' or '!'))
            {
                i = Tag(text, i, spans);
            }
            else if (razor && text[i] == '@' && i + 1 < text.Length && (char.IsLetter(text[i + 1]) || text[i + 1] == '('))
            {
                i = RazorExpression(text, i, spans);
            }
            else if (text[i] == '&')
            {
                var end = text.IndexOf(';', i);
                if (end > i && end - i <= 10)
                {
                    spans.Add(new HighlightSpan(i, end - i + 1, TokenClass.Number));
                    i = end + 1;
                }
                else
                {
                    i++;
                }
            }
            else
            {
                i++;
            }
        }

        return spans;
    }

    /// <summary>«&lt;/Имя атрибут="значение" /&gt;» — с позиции «&lt;»; возвращает позицию после тега.</summary>
    private static int Tag(string text, int start, List<HighlightSpan> spans)
    {
        var i = start + 1;
        while (i < text.Length && text[i] is '/' or '?' or '!')
            i++;
        spans.Add(new HighlightSpan(start, i - start, TokenClass.Punctuation));

        var name = i;
        while (i < text.Length && IsNameChar(text[i]))
            i++;
        if (i > name)
            spans.Add(new HighlightSpan(name, i - name, TokenClass.Keyword));

        while (i < text.Length && text[i] != '>' && text[i] != '<')
        {
            if (text[i] is '"' or '\'')
            {
                var quote = text[i];
                var end = text.IndexOf(quote, i + 1);
                end = end < 0 ? text.Length : end + 1;
                spans.Add(new HighlightSpan(i, end - i, TokenClass.String));
                i = end;
            }
            else if (IsNameChar(text[i]) || text[i] == '@')
            {
                var attribute = i;
                while (i < text.Length && (IsNameChar(text[i]) || text[i] == '@'))
                    i++;
                spans.Add(new HighlightSpan(attribute, i - attribute, TokenClass.Variable));
            }
            else
            {
                if (text[i] is '=' or '/' or '?')
                    spans.Add(new HighlightSpan(i, 1, TokenClass.Punctuation));
                i++;
            }
        }

        if (i < text.Length && text[i] == '>')
        {
            spans.Add(new HighlightSpan(i, 1, TokenClass.Punctuation));
            i++;
        }

        return i;
    }

    /// <summary>«@code», «@if» — управляющее слово; «@Model.Name», «@(x + 1)» — выражение.</summary>
    private static int RazorExpression(string text, int start, List<HighlightSpan> spans)
    {
        var i = start + 1;
        if (text[i] == '(')
        {
            var depth = 0;
            for (; i < text.Length; i++)
            {
                if (text[i] == '(')
                    depth++;
                else if (text[i] == ')' && --depth == 0)
                {
                    i++;
                    break;
                }
            }

            spans.Add(new HighlightSpan(start, i - start, TokenClass.Variable));
            return i;
        }

        while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] is '_' or '.'))
            i++;
        var word = text[(start + 1)..i].TrimEnd('.');
        spans.Add(new HighlightSpan(start, 1 + word.Length, RazorDirectives.Contains(word) ? TokenClass.ControlKeyword : TokenClass.Variable));
        return start + 1 + word.Length;
    }

    private static bool IsNameChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' or ':';

    private static bool Starts(string text, int i, string value) => string.CompareOrdinal(text, i, value, 0, value.Length) == 0;

    private static int IndexAfter(string text, int from, string end)
    {
        var index = text.IndexOf(end, from, StringComparison.Ordinal);
        return index < 0 ? text.Length : index + end.Length;
    }
}
