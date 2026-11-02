using System.Globalization;
using EaseGit.Models;

namespace EaseGit.Git.Parsers;

/// <summary>
/// Разбор git log --graph со своим форматом. Граф рисует git, а начало содержимого строк коммита и автора
/// помечено символом US (0x1F): так граф не спутать с текстом. У строк тела метки нет — граф у них не шире,
/// чем у строки коммита над ними.
/// </summary>
public static class GraphLogParser
{
    private const char Marker = '\x1f';
    private const string GraphChars = "|/\\_ ";

    /// <summary>
    /// «C» — коммит: короткий хеш, родители, ссылки (--decorate=full), заголовок; «A» — автор и дата ISO;
    /// затем тело, если есть, и пустая строка между коммитами.
    /// </summary>
    public static string[] Arguments(string revision, int limit) =>
    [
        "log",
        "--graph",
        $"-n{limit}",
        "--decorate=full",
        "--format=%x1fC%h%x1f%P%x1f%D%x1f%s%n%x1fA%an%x1f%aI%+b%n",
        revision,
        "--",
    ];

    public static IReadOnlyList<GraphLogLine> Parse(string output)
    {
        var lines = new List<GraphLogLine>();
        var graphWidth = 0;
        foreach (var raw in output.TrimEnd('\n').Split('\n'))
        {
            var marker = raw.IndexOf(Marker);
            if (marker >= 0)
            {
                var graph = raw[..marker];
                graphWidth = graph.Length;
                var fields = raw[(marker + 1)..].Split(Marker);
                var line = fields[0] switch
                {
                    ['C', .. var hash] when fields.Length >= 4 => new GraphLogLine(GraphLogLineKind.Commit, graph, fields[3])
                    {
                        ShortHash = hash,
                        IsMerge = fields[1].Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 1,
                        Refs = LogParser.ParseRefs(fields[2]),
                    },
                    ['A', .. var author] when fields.Length >= 2 => new GraphLogLine(GraphLogLineKind.Author, graph, author)
                    {
                        Date = DateTimeOffset.Parse(fields[1], CultureInfo.InvariantCulture),
                    },
                    _ => null,
                };
                if (line is not null)
                    lines.Add(line);
                continue;
            }

            // Граф строки тела — не шире, чем у коммита: иначе «| текст» с «/» или «|» в начале спутался бы с графом.
            var width = 0;
            while (width < raw.Length && width < graphWidth && GraphChars.Contains(raw[width]))
                width++;
            var text = raw[width..];
            // Соединение шире графа коммита («| | |\») — тоже граф, а не текст.
            if (text.Trim().Length > 0 && !text.All(GraphChars.Contains))
            {
                lines.Add(new GraphLogLine(GraphLogLineKind.Body, raw[..width], text.TrimEnd()));
                continue;
            }

            // Пустые строки между коммитами: из двух подряд с одинаковым графом остаётся одна.
            var graphOnly = raw.TrimEnd();
            if (lines.Count > 0 && lines[^1].Kind == GraphLogLineKind.Graph && lines[^1].Graph.TrimEnd() == graphOnly)
                continue;
            lines.Add(new GraphLogLine(GraphLogLineKind.Graph, raw.TrimEnd()));
        }

        // Пустая строка в самом конце не нужна.
        while (lines.Count > 0 && lines[^1].Kind == GraphLogLineKind.Graph && lines[^1].Graph.Trim('|', ' ').Length == 0)
            lines.RemoveAt(lines.Count - 1);
        return lines;
    }
}
