using EaseGit.Models;
using EaseGit.UI.Panels;

namespace EaseGit.Tests.UI.Panels;

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

    [Fact]
    public void Build_Filter_KeepsMatchingPathsIgnoringCaseAndCollapsedFolders()
    {
        var collapsed = new HashSet<(FileSection, string)> { (FileSection.Unstaged, "EaseGit/UI") };

        var rows = FileRows.Build(Status(
            Modified("EaseGit/UI/Panels/FilesPanel.cs"),
            Modified("EaseGit/UI/MainWindow.cs"),
            Modified("README.md")), collapsed, filter: "panel");

        Assert.Equal(["EaseGit/UI/Panels/", "  FilesPanel.cs"], Tree(rows));
        Assert.Equal(1, rows.OfType<SectionHeaderRow>().Single().Count);
    }

    [Fact]
    public void Build_FilterMatchesOldPathOfRename()
    {
        var renamed = new FileChange("docs/new.md", ChangeKind.Renamed, ChangeKind.Unmodified, "docs/old.md");

        var rows = FileRows.Build(Status(renamed, Modified("a.txt")), filter: "OLD");

        Assert.Equal("docs/new.md", rows.OfType<FileEntryRow>().Single().File.Path);
    }

    private static FileChange Modified(string path) => File(path, ChangeKind.Unmodified, ChangeKind.Modified);

    /// <summary>Строки без заголовков разделов в виде «отступ + имя», у папок — «/» в конце.</summary>
    private static string[] Tree(IEnumerable<FileRow> rows) => rows
        .Select(r => r switch
        {
            DirectoryRow d => new string(' ', d.Depth * 2) + d.Name + "/",
            FileEntryRow f => new string(' ', f.Depth * 2) + f.Name,
            _ => null,
        })
        .OfType<string>()
        .ToArray();

    [Fact]
    public void Build_NestedFiles_DirectoriesFirstWithIndent()
    {
        var rows = FileRows.Build(Status(
            Modified("readme.md"),
            Modified("src/b.cs"),
            Modified("src/ui/view.cs"),
            Modified("src/a.cs")));

        Assert.Equal(["src/", "  ui/", "    view.cs", "  a.cs", "  b.cs", "readme.md"], Tree(rows));
    }

    [Fact]
    public void Build_SingleNestedDirectory_MergedIntoOneRow()
    {
        var rows = FileRows.Build(Status(
            Modified("EaseGit/UI/Panels/A.cs"),
            Modified("EaseGit/UI/Panels/B.cs")));

        Assert.Equal(["EaseGit/UI/Panels/", "  A.cs", "  B.cs"], Tree(rows));
        Assert.Equal("EaseGit/UI/Panels", rows.OfType<DirectoryRow>().Single().Path);
    }

    [Fact]
    public void Build_CollapsedDirectory_HidesContentsButKeepsFiles()
    {
        var status = Status(Modified("src/a.cs"), Modified("src/ui/b.cs"), Modified("c.txt"));

        var rows = FileRows.Build(status, new HashSet<(FileSection, string)> { (FileSection.Unstaged, "src") });

        Assert.Equal(["src/", "c.txt"], Tree(rows));
        var directory = rows.OfType<DirectoryRow>().Single();
        Assert.True(directory.Collapsed);
        Assert.Equal(["src/a.cs", "src/ui/b.cs"], directory.Files.Select(f => f.File.Path));
    }

    [Fact]
    public void Build_SameDirectoryInTwoSections_CollapsedSeparately()
    {
        var status = Status(File("src/a.cs", ChangeKind.Modified, ChangeKind.Modified));

        var rows = FileRows.Build(status, new HashSet<(FileSection, string)> { (FileSection.Staged, "src") });

        Assert.Equal(["src/", "src/", "  a.cs"], Tree(rows));
    }

    [Fact]
    public void Entries_IncludesFilesFromAllSectionsFlat()
    {
        var entries = FileRows.Entries(Status(
            Modified("src/ui/b.cs"),
            File("new/c.txt", ChangeKind.Unmodified, ChangeKind.Untracked)));

        Assert.Equal(
            [(FileSection.Unstaged, "src/ui/b.cs"), (FileSection.Untracked, "new/c.txt")],
            entries.Select(e => (e.Section, e.File.Path)));
        Assert.All(entries, e => Assert.Equal(0, e.Depth));
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
