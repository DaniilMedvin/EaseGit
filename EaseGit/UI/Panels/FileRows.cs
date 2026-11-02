using EaseGit.Models;

namespace EaseGit.UI.Panels;

/// <summary>Раздел панели файлов. Порядок значений задаёт порядок разделов на экране.</summary>
public enum FileSection
{
    Conflicts,
    Staged,
    Unstaged,
    Untracked,
}

/// <summary>Строка панели файлов: заголовок раздела, папка или файл.</summary>
public abstract record FileRow;

public sealed record SectionHeaderRow(FileSection Section, int Count) : FileRow;

/// <summary>
/// Папка в дереве раздела. Цепочка папок с единственной вложенной папкой схлопывается в одну строку:
/// <see cref="Name"/> = «EaseGit/UI/Panels».
/// </summary>
/// <param name="Path">Путь папки от корня репозитория, без «/» в конце.</param>
/// <param name="Name">Что показывается в строке.</param>
/// <param name="Depth">Уровень вложенности, 0 — сразу под заголовком раздела.</param>
/// <param name="Files">Все файлы раздела внутри папки, включая вложенные.</param>
public sealed record DirectoryRow(
    FileSection Section, string Path, string Name, int Depth, bool Collapsed, IReadOnlyList<FileEntryRow> Files) : FileRow;

public sealed record FileEntryRow(FileSection Section, FileChange File, int Depth = 0) : FileRow
{
    /// <summary>Вид изменения, который показывается в этом разделе.</summary>
    public ChangeKind Kind => Section switch
    {
        FileSection.Conflicts => ChangeKind.Unmerged,
        FileSection.Staged => File.Staged,
        FileSection.Untracked => ChangeKind.Untracked,
        _ => File.Unstaged,
    };

    /// <summary>Имя файла без папки.</summary>
    public string Name => System.IO.Path.GetFileName(File.Path);
}

public static class FileRows
{
    /// <summary>
    /// Все файлы по разделам, без дерева и сворачивания. Файл со staged и unstaged изменениями
    /// попадает в оба раздела. Внутри раздела файлы отсортированы по пути.
    /// </summary>
    public static IReadOnlyList<FileEntryRow> Entries(RepoStatus status) =>
        Enum.GetValues<FileSection>()
            .SelectMany(section => status.Files
                .Where(f => IsInSection(f, section))
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .Select(f => new FileEntryRow(section, f)))
            .ToList();

    /// <summary>
    /// Строки панели: заголовок раздела, под ним дерево папок и файлов. Пустые разделы не показываются.
    /// На каждом уровне сначала папки, затем файлы, по алфавиту.
    /// </summary>
    /// <param name="collapsed">Свёрнутые папки: раздел и путь папки. Их содержимое не выводится.</param>
    /// <param name="filter">
    /// Только файлы, в пути которых есть этот текст (без учёта регистра). С фильтром папки не сворачиваются: найденное видно сразу.
    /// </param>
    public static IReadOnlyList<FileRow> Build(RepoStatus status, IReadOnlySet<(FileSection, string)>? collapsed = null, string? filter = null)
    {
        var rows = new List<FileRow>();
        var files = Entries(status);
        if (!string.IsNullOrEmpty(filter))
        {
            files = files.Where(e => PanelFilter.Matches(e.File.Path, filter) || e.File.OriginalPath is { } old && PanelFilter.Matches(old, filter)).ToList();
            collapsed = null;
        }

        foreach (var group in files.GroupBy(e => e.Section))
        {
            var entries = group.ToList();
            rows.Add(new SectionHeaderRow(group.Key, entries.Count));
            AddLevel(rows, group.Key, entries, "", 0, collapsed);
        }

        return rows;
    }

    private static void AddLevel(
        List<FileRow> rows, FileSection section, IReadOnlyList<FileEntryRow> entries, string prefix, int depth,
        IReadOnlySet<(FileSection, string)>? collapsed)
    {
        var directories = entries
            .Where(e => SubdirectoryOf(e, prefix) is not null)
            .GroupBy(e => SubdirectoryOf(e, prefix)!, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal);

        foreach (var group in directories)
        {
            var files = group.ToList();
            var name = group.Key;
            var path = prefix + name;
            while (SingleSubdirectory(files, path + "/") is { } nested)
            {
                name += "/" + nested;
                path += "/" + nested;
            }

            var isCollapsed = collapsed?.Contains((section, path)) == true;
            rows.Add(new DirectoryRow(section, path, name, depth, isCollapsed, files));
            if (!isCollapsed)
                AddLevel(rows, section, files, path + "/", depth + 1, collapsed);
        }

        rows.AddRange(entries
            .Where(e => SubdirectoryOf(e, prefix) is null)
            .Select(e => e with { Depth = depth }));
    }

    /// <summary>Первая папка в пути файла после <paramref name="prefix"/> или null, если файл лежит прямо в нём.</summary>
    private static string? SubdirectoryOf(FileEntryRow entry, string prefix)
    {
        var slash = entry.File.Path.IndexOf('/', prefix.Length);
        return slash < 0 ? null : entry.File.Path[prefix.Length..slash];
    }

    /// <summary>Имя папки, если все файлы лежат в одной и той же вложенной папке, иначе null.</summary>
    private static string? SingleSubdirectory(IReadOnlyList<FileEntryRow> files, string prefix)
    {
        var first = SubdirectoryOf(files[0], prefix);
        return first is not null && files.All(f => SubdirectoryOf(f, prefix) == first) ? first : null;
    }

    private static bool IsInSection(FileChange file, FileSection section) => section switch
    {
        FileSection.Conflicts => file.IsConflicted,
        FileSection.Staged => !file.IsConflicted && file.HasStagedChanges,
        FileSection.Unstaged => !file.IsConflicted && !file.IsUntracked && file.HasUnstagedChanges,
        FileSection.Untracked => file.IsUntracked,
        _ => false,
    };
}
