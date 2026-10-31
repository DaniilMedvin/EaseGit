using LazyGit.Models;
using LazyGit.UI.Panels;

namespace LazyGit.Tests.UI.Panels;

public class FileRowsTests
{
    private static RepoStatus Status(params FileChange[] files) => new("main", null, null, 0, 0, files);

    private static FileChange File(string path, ChangeKind staged, ChangeKind unstaged) => new(path, staged, unstaged);

    [Fact]
    public void Build_NoFiles_ReturnsNoRows()
    {
        Assert.Empty(FileRows.Build(Status()));
    }

    [Fact]
    public void Build_SectionsInOrder_WithHeadersAndCounts()
    {
        var rows = FileRows.Build(Status(
            File("untracked.txt", ChangeKind.Unmodified, ChangeKind.Untracked),
            File("modified.txt", ChangeKind.Unmodified, ChangeKind.Modified),
            File("staged.txt", ChangeKind.Added, ChangeKind.Unmodified),
            File("conflict.txt", ChangeKind.Unmerged, ChangeKind.Unmerged)));

        var headers = rows.OfType<SectionHeaderRow>().Select(h => (h.Section, h.Count));
        Assert.Equal(
            [(FileSection.Conflicts, 1), (FileSection.Staged, 1), (FileSection.Unstaged, 1), (FileSection.Untracked, 1)],
            headers);
        Assert.IsType<SectionHeaderRow>(rows[0]);
    }

    [Fact]
    public void Build_StagedAndUnstagedFile_AppearsInBothSections()
    {
        var rows = FileRows.Build(Status(File("a.txt", ChangeKind.Modified, ChangeKind.Modified)));

        var sections = rows.OfType<FileEntryRow>().Select(r => r.Section);
        Assert.Equal([FileSection.Staged, FileSection.Unstaged], sections);
    }

    [Fact]
    public void Build_ConflictedFile_AppearsOnlyInConflicts()
    {
        var rows = FileRows.Build(Status(File("c.txt", ChangeKind.Unmerged, ChangeKind.Unmerged)));

        var entry = Assert.Single(rows.OfType<FileEntryRow>());
        Assert.Equal(FileSection.Conflicts, entry.Section);
        Assert.Equal(ChangeKind.Unmerged, entry.Kind);
    }

    [Fact]
    public void Build_FilesInSection_SortedByPath()
    {
        var rows = FileRows.Build(Status(
            File("b.txt", ChangeKind.Unmodified, ChangeKind.Modified),
            File("a.txt", ChangeKind.Unmodified, ChangeKind.Modified)));

        Assert.Equal(["a.txt", "b.txt"], rows.OfType<FileEntryRow>().Select(r => r.File.Path));
    }

    [Theory]
    [InlineData(FileSection.Staged, ChangeKind.Added)]
    [InlineData(FileSection.Unstaged, ChangeKind.Modified)]
    public void Kind_DependsOnSection(FileSection section, ChangeKind expected)
    {
        var row = new FileEntryRow(section, File("a.txt", ChangeKind.Added, ChangeKind.Modified));

        Assert.Equal(expected, row.Kind);
    }
}
