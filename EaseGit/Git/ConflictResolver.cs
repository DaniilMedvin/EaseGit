using EaseGit.Models;

namespace EaseGit.Git;

/// <summary>
/// Конфликт внутри файла: разбор маркеров git, выбор стороны для одного блока, сборка текста обратно.
/// Чистые функции без IO. Переводы строк (LF / CRLF) сохраняются как были.
/// </summary>
public static class ConflictResolver
{
    private const int MarkerSize = 7;

    /// <summary>
    /// Части файла или null, если маркеры не сходятся (блок не закрыт, лишний «=======»): такой файл безопаснее
    /// решать целиком или в редакторе.
    /// </summary>
    public static IReadOnlyList<ConflictPart>? Parse(string text)
    {
        var parts = new List<ConflictPart>();
        var plain = new List<string>();
        var lines = SplitKeepingEndings(text);
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IsMarker(lines[i], '<'))
            {
                plain.Add(lines[i]);
                continue;
            }

            var block = ReadBlock(lines, ref i);
            if (block is null)
                return null;

            if (plain.Count > 0)
                parts.Add(new PlainPart(plain.ToList()));
            plain.Clear();
            parts.Add(block);
        }

        if (plain.Count > 0)
            parts.Add(new PlainPart(plain));
        return parts;
    }

    /// <summary>Блок с позиции <paramref name="i"/> (строка «&lt;&lt;&lt;»); после — <paramref name="i"/> на строке «&gt;&gt;&gt;».</summary>
    private static ConflictBlock? ReadBlock(IReadOnlyList<string> lines, ref int i)
    {
        var oursMarker = lines[i];
        List<string> ours = [], theirs = [];
        List<string>? @base = null;
        string? baseMarker = null, separator = null;
        var current = ours;
        for (i++; i < lines.Count; i++)
        {
            var line = lines[i];
            if (IsMarker(line, '|') && separator is null && baseMarker is null)
            {
                baseMarker = line;
                current = @base = [];
            }
            else if (IsMarker(line, '=') && separator is null)
            {
                separator = line;
                current = theirs;
            }
            else if (IsMarker(line, '>') && separator is not null)
            {
                return new ConflictBlock(oursMarker, ours, baseMarker, @base, separator, theirs, line);
            }
            else if (IsMarker(line, '<') || (IsMarker(line, '=') && separator is not null))
            {
                return null;
            }
            else
            {
                current.Add(line);
            }
        }

        return null;
    }

    /// <summary>Маркер — ровно 7 одинаковых символов в начале строки, дальше пробел или конец строки.</summary>
    private static bool IsMarker(string line, char c)
    {
        if (line.Length < MarkerSize || line.Take(MarkerSize).Any(ch => ch != c))
            return false;

        return line.Length == MarkerSize || line[MarkerSize] is ' ' or '\r' or '\n';
    }

    public static int CountBlocks(IReadOnlyList<ConflictPart> parts) => parts.Count(p => p is ConflictBlock);

    /// <summary>Заменяет блок номер <paramref name="block"/> (с нуля) выбранными строками.</summary>
    public static IReadOnlyList<ConflictPart> Resolve(IReadOnlyList<ConflictPart> parts, int block, ConflictChoice choice)
    {
        var result = new List<ConflictPart>();
        var index = 0;
        foreach (var part in parts)
        {
            if (part is ConflictBlock conflict && index++ == block)
            {
                IReadOnlyList<string> kept = choice switch
                {
                    ConflictChoice.Ours => conflict.Ours,
                    ConflictChoice.Theirs => conflict.Theirs,
                    // За нашими строками в блоке всегда шёл маркер, так что последняя из них с переводом строки.
                    _ => [.. conflict.Ours, .. conflict.Theirs],
                };
                result.Add(new PlainPart(kept));
            }
            else
            {
                result.Add(part);
            }
        }

        return result;
    }

    /// <summary>Текст файла из частей: решённые блоки — выбранными строками, остальные — с маркерами, как были.</summary>
    public static string Render(IReadOnlyList<ConflictPart> parts)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            switch (part)
            {
                case PlainPart plain:
                    foreach (var line in plain.Lines)
                        builder.Append(line);
                    break;
                case ConflictBlock block:
                    builder.Append(block.OursMarker);
                    foreach (var line in block.Ours)
                        builder.Append(line);
                    if (block.BaseMarker is not null)
                    {
                        builder.Append(block.BaseMarker);
                        foreach (var line in block.Base ?? [])
                            builder.Append(line);
                    }

                    builder.Append(block.Separator);
                    foreach (var line in block.Theirs)
                        builder.Append(line);
                    builder.Append(block.TheirsMarker);
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Строки вместе с «\n» (и «\r» перед ним); последняя — без перевода строки, если файл им не кончается.</summary>
    public static IReadOnlyList<string> SplitKeepingEndings(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n')
                continue;

            lines.Add(text[start..(i + 1)]);
            start = i + 1;
        }

        if (start < text.Length)
            lines.Add(text[start..]);
        return lines;
    }
}
