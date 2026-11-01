using LazyGit.Models;

namespace LazyGit.Git.Parsers;

/// <summary>
/// Файлы сравнения: <c>git diff --name-status -z</c> (статус и пути) и <c>git diff --numstat -z</c> (сколько строк),
/// сведённые по пути. У переименования в numstat путь пуст, а старое и новое имя идут следом через NUL.
/// </summary>
public static class CompareParser
{
    /// <summary>«main...feature» — от общего предка (что принесла ветка), «main..feature» — между вершинами.</summary>
    public static string Range(string left, string right, bool fromMergeBase) => fromMergeBase ? $"{left}...{right}" : $"{left}..{right}";

    public static string[] NameStatusArguments(string range) => ["diff", "--name-status", "-z", "--find-renames", range, "--"];

    public static string[] NumstatArguments(string range) => ["diff", "--numstat", "-z", "--find-renames", range, "--"];

    public static IReadOnlyList<ComparedFile> Parse(string nameStatus, string numstat)
    {
        var counts = ParseNumstat(numstat);
        var files = new List<ComparedFile>();
        var items = nameStatus.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < items.Length; i++)
        {
            var status = items[i][0];
            string path;
            string? old = null;
            if (status is 'R' or 'C' && i + 2 < items.Length)
            {
                old = items[++i];
                path = items[++i];
            }
            else if (i + 1 < items.Length)
            {
                path = items[++i];
            }
            else
            {
                break;
            }

            var (added, deleted) = counts.GetValueOrDefault(path, (null, null));
            files.Add(new ComparedFile(status, path, old, added, deleted));
        }

        return files;
    }

    /// <summary>Путь (новое имя у переименования) → добавлено и удалено строк; «-» у бинарного — null.</summary>
    private static Dictionary<string, (int?, int?)> ParseNumstat(string output)
    {
        var result = new Dictionary<string, (int?, int?)>();
        var items = output.Split('\0');
        for (var i = 0; i < items.Length; i++)
        {
            var fields = items[i].TrimStart('\n').Split('\t');
            if (fields.Length < 3)
                continue;

            int? added = int.TryParse(fields[0], out var a) ? a : null;
            int? deleted = int.TryParse(fields[1], out var d) ? d : null;
            var path = fields[2];
            // Переименование: «a\td\t» и затем отдельными элементами старое и новое имя.
            if (path.Length == 0 && i + 2 < items.Length)
            {
                i++;
                path = items[++i];
            }

            result[path] = (added, deleted);
        }

        return result;
    }
}
