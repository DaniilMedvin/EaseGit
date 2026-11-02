using EaseGit.Models;

namespace EaseGit.UI.Panels;

/// <summary>Символ графа и номер цвета его ветки. -1 — пробел.</summary>
public readonly record struct GraphCell(char Symbol, int Color);

/// <summary>
/// Граф истории для панели коммитов: одна строка на коммит, каждая дорожка — два символа (линия и промежуток).
/// Строится по родителям коммитов, а не из <c>git log --graph</c>: у того бывают строки без коммитов,
/// а здесь строка графа должна совпадать со строкой списка.
/// Цвет закреплён за веткой, а не за колонкой: дорожка получает новый цвет, когда появляется,
/// и сохраняет его, даже если сдвигается. Линия текущей ветки (от HEAD по первым родителям) всегда цвета 0
/// и при слиянии дорожек продолжается сама, а другие вливаются в неё — как в Git Graph Visual Studio.
/// </summary>
public static class CommitGraph
{
    public const char Commit = '●';
    public const char Merge = '◉';

    /// <summary>
    /// Дорожка: хеш коммита, который на ней ожидается ниже (null — свободна), цвет её ветки
    /// и признак линии текущей ветки.
    /// </summary>
    private record struct Lane(string? Hash, int Color, bool Head = false);

    private static bool IsHead(Commit commit) => commit.Refs.Any(r => r.IsCurrent || r.Kind == RefKind.Head);

    /// <param name="commits">Коммиты в порядке --topo-order: родитель ниже всех своих детей.</param>
    public static IReadOnlyList<IReadOnlyList<GraphCell>> Build(IReadOnlyList<Commit> commits)
    {
        var lanes = new List<Lane>();
        // Цвет 0 оставлен линии текущей ветки, если HEAD есть в списке.
        var nextColor = commits.Any(IsHead) ? 1 : 0;
        var rows = new List<IReadOnlyList<GraphCell>>(commits.Count);
        foreach (var commit in commits)
        {
            var before = lanes.ToList();
            // Если коммит ждут несколько дорожек, продолжается линия текущей ветки, иначе самая левая.
            var column = lanes.FindIndex(l => l.Hash == commit.Hash && l.Head);
            if (column < 0)
                column = lanes.FindIndex(l => l.Hash == commit.Hash);
            if (column < 0)
            {
                // Вершина ветки, которую никто не ждал: новая ветка — новый цвет.
                column = FreeLane(lanes, 0);
                lanes[column] = IsHead(commit) ? new Lane(null, 0, Head: true) : new Lane(null, nextColor++);
            }
            else if (IsHead(commit))
            {
                // HEAD внутри чужой линии (например, detached на коммите ветки): дальше вниз — линия текущей ветки.
                lanes[column] = lanes[column] with { Head = true };
            }

            // Другие дорожки, ждавшие этот коммит, сходятся в него и заканчиваются.
            var merging = new List<int>();
            for (var i = 0; i < lanes.Count; i++)
            {
                if (i != column && lanes[i].Hash == commit.Hash)
                {
                    merging.Add(i);
                    lanes[i] = default;
                }
            }

            // Первый родитель продолжает ветку коммита тем же цветом.
            lanes[column] = lanes[column] with { Hash = commit.Parents.Count > 0 ? commit.Parents[0] : null };

            // Второй и следующие родители merge: в уже идущую дорожку или в новую справа своим цветом.
            var targets = new List<int>();
            foreach (var parent in commit.Parents.Skip(1))
            {
                var lane = lanes.FindIndex(l => l.Hash == parent);
                if (lane < 0)
                {
                    lane = FreeLane(lanes, column + 1);
                    lanes[lane] = new Lane(parent, nextColor++);
                }

                targets.Add(lane);
            }

            var commitColor = lanes[column].Color;
            while (lanes.Count > 0 && lanes[^1].Hash is null)
                lanes.RemoveAt(lanes.Count - 1);

            rows.Add(Row(before, lanes, column, commitColor, commit.Parents.Count > 1, merging, targets));
        }

        return rows;
    }

    /// <summary>Первая свободная дорожка не левее <paramref name="from"/>; если нет — новая справа.</summary>
    private static int FreeLane(List<Lane> lanes, int from)
    {
        for (var i = from; i < lanes.Count; i++)
            if (lanes[i].Hash is null)
                return i;

        while (lanes.Count <= from)
            lanes.Add(default);
        if (lanes[^1].Hash is not null)
            lanes.Add(default);
        return lanes.Count - 1;
    }

    private static IReadOnlyList<GraphCell> Row(List<Lane> before, List<Lane> after, int column, int commitColor, bool isMerge,
        List<int> merging, List<int> targets)
    {
        var ends = merging.Concat(targets).ToList();
        var low = ends.Count > 0 ? Math.Min(column, ends.Min()) : column;
        var high = ends.Count > 0 ? Math.Max(column, ends.Max()) : column;
        var width = Math.Max(Math.Max(before.Count, after.Count), high + 1);

        bool Up(int i) => i < before.Count && before[i].Hash is not null;
        bool Down(int i) => i < after.Count && after[i].Hash is not null;

        // Цвет дорожки: сходящаяся уже закончилась, поэтому её цвет — из строки выше.
        int ColorOf(int i) => i == column ? commitColor
            : merging.Contains(i) || !Down(i) ? (i < before.Count ? before[i].Color : -1)
            : after[i].Color;

        // Горизонтальная линия красится цветом дальней дорожки со своей стороны.
        var leftColor = ColorOf(low);
        var rightColor = ColorOf(high);

        var cells = new List<GraphCell>(width * 2);
        for (var i = 0; i < width; i++)
        {
            if (i == column)
                cells.Add(new GraphCell(isMerge ? Merge : Commit, commitColor));
            else if (merging.Contains(i))
                cells.Add(new GraphCell(Down(i) ? (i > column ? '┤' : '├') : (i > column ? '╯' : '╰'), ColorOf(i)));
            else if (targets.Contains(i))
                cells.Add(new GraphCell(Up(i) ? (i > column ? '┤' : '├') : (i > column ? '╮' : '╭'), ColorOf(i)));
            else if (i > low && i < high)
                cells.Add(Up(i) || Down(i) ? new GraphCell('┼', ColorOf(i)) : new GraphCell('─', i < column ? leftColor : rightColor));
            else if (Up(i) || Down(i))
                cells.Add(new GraphCell('│', ColorOf(i)));
            else
                cells.Add(new GraphCell(' ', -1));

            cells.Add(i >= low && i < high ? new GraphCell('─', i < column ? leftColor : rightColor) : new GraphCell(' ', -1));
        }

        // Хвостовые пробелы не нужны: текст строки начинается сразу после графа.
        while (cells.Count > 2 && cells[^1].Symbol == ' ' && cells[^2].Symbol == ' ')
            cells.RemoveRange(cells.Count - 2, 2);
        return cells;
    }
}
