using LazyGit.Models;

namespace LazyGit.UI.Panels;

/// <summary>Раздел панели файлов. Порядок значений задаёт порядок разделов на экране.</summary>
public enum FileSection
{
    Conflicts,
    Staged,
    Unstaged,
    Untracked,
}

/// <summary>Строка панели файлов: заголовок раздела или файл.</summary>
public abstract record FileRow;

public sealed record SectionHeaderRow(FileSection Section, int Count) : FileRow;

public sealed record FileEntryRow(FileSection Section, FileChange File) : FileRow
{
    /// <summary>Вид изменения, который показывается в этом разделе.</summary>
    public ChangeKind Kind => Section switch
    {
        FileSection.Conflicts => ChangeKind.Unmerged,
        FileSection.Staged => File.Staged,
        FileSection.Untracked => ChangeKind.Untracked,
        _ => File.Unstaged,
    };
}

public static class FileRows
{
    /// <summary>
    /// Группирует файлы по разделам. Файл со staged и unstaged изменениями попадает в оба раздела.
    /// Пустые разделы не показываются, файлы внутри раздела отсортированы по пути.
    /// </summary>
    public static IReadOnlyList<FileRow> Build(RepoStatus status)
    {
        var rows = new List<FileRow>();
        foreach (var section in Enum.GetValues<FileSection>())
        {
            var files = status.Files
                .Where(f => IsInSection(f, section))
                .OrderBy(f => f.Path, StringComparer.Ordinal)
                .ToList();
            if (files.Count == 0)
                continue;

            rows.Add(new SectionHeaderRow(section, files.Count));
            rows.AddRange(files.Select(f => new FileEntryRow(section, f)));
        }

        return rows;
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
