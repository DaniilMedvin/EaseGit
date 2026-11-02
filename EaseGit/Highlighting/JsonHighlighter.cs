namespace EaseGit.Highlighting;

/// <summary>
/// Подсветка JSON (и JSON с комментариями, как appsettings.json): ключ — переменная, строка — строка, число — число,
/// true / false / null — ключевое слово, комментарии — комментарий, скобки и двоеточия — пунктуация.
/// </summary>
public static class JsonHighlighter
{
    public static IReadOnlyList<HighlightSpan> Highlight(string text)
    {
        var spans = new List<HighlightSpan>();
        var i = 0;
        while (i < text.Length)
        {
            var c = text[i];
            if (c == '"')
            {
                var end = i + 1;
                while (end < text.Length && text[end] != '"' && text[end] != '\n')
                    end += text[end] == '\\' ? 2 : 1;
                end = Math.Min(text.Length, end + 1);
                // Ключ — строка, после которой (через пробелы) двоеточие.
                var next = end;
                while (next < text.Length && text[next] is ' ' or '\t')
                    next++;
                spans.Add(new HighlightSpan(i, end - i, next < text.Length && text[next] == ':' ? TokenClass.Variable : TokenClass.String));
                i = end;
            }
            else if (c == '/' && i + 1 < text.Length && text[i + 1] is '/' or '*')
            {
                var end = text[i + 1] == '/' ? text.IndexOf('\n', i) : text.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? text.Length : text[i + 1] == '/' ? end : end + 2;
                spans.Add(new HighlightSpan(i, end - i, TokenClass.Comment));
                i = end;
            }
            else if (char.IsDigit(c) || (c == '-' && i + 1 < text.Length && char.IsDigit(text[i + 1])))
            {
                var end = i + 1;
                while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] is '.' or '+' or '-'))
                    end++;
                spans.Add(new HighlightSpan(i, end - i, TokenClass.Number));
                i = end;
            }
            else if (char.IsLetter(c))
            {
                var end = i;
                while (end < text.Length && char.IsLetter(text[end]))
                    end++;
                if (text[i..end] is "true" or "false" or "null")
                    spans.Add(new HighlightSpan(i, end - i, TokenClass.Keyword));
                i = end;
            }
            else
            {
                if (c is '{' or '}' or '[' or ']' or ':' or ',')
                    spans.Add(new HighlightSpan(i, 1, TokenClass.Punctuation));
                i++;
            }
        }

        return spans;
    }
}
