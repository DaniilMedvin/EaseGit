using LazyGit.Models;

namespace LazyGit.UI.Panels;

/// <summary>Символ графа и номер дорожки, по которому выбирается цвет. -1 — пробел.</summary>
public readonly record struct GraphCell(char Symbol, int Lane);

/// <summary>
/// Граф истории для панели коммитов: одна строка на коммит, каждая дорожка — два символа (линия и промежуток).
/// Строится по родителям коммитов, а не из <c>git log --graph</c>: у того бывают строки без коммитов,
/// а здесь строка графа должна совпадать со строкой списка.
/// </summary>
public static class CommitGraph
{
    public const char Commit = '●';
    public const char Merge = '◉';

    /// <param name="commits">Коммиты в порядке --topo-order: родитель ниже всех своих детей.</param>
    public static IReadOnlyList<IReadOnlyList<GraphCell>> Build(IReadOnlyList<Commit> commits)
    {
        // Дорожка хранит хеш коммита, который на ней ожидается ниже; null — свободна.
        var lanes = new List<string?>();
        var rows = new List<IReadOnlyList<GraphCell>>(commits.Count);
        foreach (var commit in commits)
        {
            var before = lanes.ToList();
            var column = lanes.IndexOf(commit.Hash);
            if (column < 0)
                column = FreeLane(lanes, 0);

            // Другие дорожки, ждавшие этот коммит, сходятся в него и заканчиваются.
            var merging = new List<int>();
            for (var i = 0; i < lanes.Count; i++)
            {
                if (i != column && lanes[i] == commit.Hash)
                {
                    merging.Add(i);
                    lanes[i] = null;
                }
            }

            lanes[column] = commit.Parents.Count > 0 ? commit.Parents[0] : null;

            // Второй и следующие родители merge: в уже идущую дорожку или в новую справа.
            var targets = new List<int>();
            foreach (var parent in commit.Parents.Skip(1))
            {
                var lane = lanes.IndexOf(parent);
                if (lane < 0)
                {
                    lane = FreeLane(lanes, column + 1);
                    lanes[lane] = parent;
                }

                targets.Add(lane);
            }

            while (lanes.Count > 0 && lanes[^1] is null)
                lanes.RemoveAt(lanes.Count - 1);

            rows.Add(Row(before, lanes, column, commit.Parents.Count > 1, merging, targets));
        }

        return rows;
    }

    /// <summary>Первая свободная дорожка не левее <paramref name="from"/>; если нет — новая справа.</summary>
    private static int FreeLane(List<string?> lanes, int from)
    {
        for (var i = from; i < lanes.Count; i++)
            if (lanes[i] is null)
                return i;

        while (lanes.Count < from)
            lanes.Add(null);
        lanes.Add(null);
        return lanes.Count - 1;
    }

    private static IReadOnlyList<GraphCell> Row(List<string?> before, List<string?> after, int column, bool isMerge,
        List<int> merging, List<int> targets)
    {
        var ends = merging.Concat(targets).ToList();
        var low = ends.Count > 0 ? Math.Min(column, ends.Min()) : column;
        var high = ends.Count > 0 ? Math.Max(column, ends.Max()) : column;
        var width = Math.Max(Math.Max(before.Count, after.Count), high + 1);

        bool Up(int i) => i < before.Count && before[i] is not null;
        bool Down(int i) => i < after.Count && after[i] is not null;

        // Горизонтальная линия красится цветом дальней дорожки со своей стороны.
        int HorizontalLane(int i) => i < column ? low : high;

        var cells = new List<GraphCell>(width * 2);
        for (var i = 0; i < width; i++)
        {
            if (i == column)
                cells.Add(new GraphCell(isMerge ? Merge : Commit, i));
            else if (merging.Contains(i))
                cells.Add(new GraphCell(Down(i) ? (i > column ? '┤' : '├') : (i > column ? '╯' : '╰'), i));
            else if (targets.Contains(i))
                cells.Add(new GraphCell(Up(i) ? (i > column ? '┤' : '├') : (i > column ? '╮' : '╭'), i));
            else if (i > low && i < high)
                cells.Add(Up(i) || Down(i) ? new GraphCell('┼', i) : new GraphCell('─', HorizontalLane(i)));
            else if (Up(i) || Down(i))
                cells.Add(new GraphCell('│', i));
            else
                cells.Add(new GraphCell(' ', -1));

            cells.Add(i >= low && i < high ? new GraphCell('─', i < column ? low : high) : new GraphCell(' ', -1));
        }

        // Хвостовые пробелы не нужны: текст строки начинается сразу после графа.
        while (cells.Count > 2 && cells[^1].Symbol == ' ' && cells[^2].Symbol == ' ')
            cells.RemoveRange(cells.Count - 2, 2);
        return cells;
    }
}
