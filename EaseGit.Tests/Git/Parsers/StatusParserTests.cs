using EaseGit.Git.Parsers;
using EaseGit.Models;

namespace EaseGit.Tests.Git.Parsers;

public class StatusParserTests
{
    private const string Hash = "28e1a63bec7b444b8884880e4761a46ced830c2d";
    private const string Blob = "78981922613b2afb6025042ff6bd878ac1994e85";

    /// <summary>Склеивает записи так же, как git status с флагом -z.</summary>
    private static string Z(params string[] records) => string.Join('\0', records) + '\0';

    private static string Ordinary(string xy, string path) =>
        $"1 {xy} N... 100644 100644 100644 {Blob} {Blob} {path}";

    [Fact]
    public void Parse_BranchHeaders_ReturnsBranchAndHead()
    {
        var status = StatusParser.Parse(Z($"# branch.oid {Hash}", "# branch.head main"));

        Assert.Equal("main", status.Branch);
        Assert.Equal(Hash, status.HeadHash);
        Assert.Null(status.Upstream);
        Assert.True(status.IsClean);
    }

    [Fact]
    public void Parse_Upstream_ReturnsAheadAndBehind()
    {
        var status = StatusParser.Parse(Z(
            $"# branch.oid {Hash}",
            "# branch.head main",
            "# branch.upstream origin/main",
            "# branch.ab +2 -1"));

        Assert.Equal("origin/main", status.Upstream);
        Assert.Equal(2, status.Ahead);
        Assert.Equal(1, status.Behind);
    }

    [Fact]
    public void Parse_DetachedHead_ReturnsNullBranch()
    {
        var status = StatusParser.Parse(Z($"# branch.oid {Hash}", "# branch.head (detached)"));

        Assert.Null(status.Branch);
        Assert.True(status.IsDetached);
    }

    [Fact]
    public void Parse_EmptyRepository_ReturnsNullHeadHash()
    {
        var status = StatusParser.Parse(Z("# branch.oid (initial)", "# branch.head main"));

        Assert.Null(status.HeadHash);
        Assert.Equal("main", status.Branch);
    }

    [Theory]
    [InlineData("M.", ChangeKind.Modified, ChangeKind.Unmodified)]
    [InlineData(".M", ChangeKind.Unmodified, ChangeKind.Modified)]
    [InlineData("MM", ChangeKind.Modified, ChangeKind.Modified)]
    [InlineData("A.", ChangeKind.Added, ChangeKind.Unmodified)]
    [InlineData("AM", ChangeKind.Added, ChangeKind.Modified)]
    [InlineData("D.", ChangeKind.Deleted, ChangeKind.Unmodified)]
    [InlineData(".D", ChangeKind.Unmodified, ChangeKind.Deleted)]
    [InlineData(".T", ChangeKind.Unmodified, ChangeKind.TypeChanged)]
    public void Parse_OrdinaryEntry_ReturnsStagedAndUnstagedKinds(string xy, ChangeKind staged, ChangeKind unstaged)
    {
        var file = Assert.Single(StatusParser.Parse(Z(Ordinary(xy, "a.txt"))).Files);

        Assert.Equal("a.txt", file.Path);
        Assert.Equal(staged, file.Staged);
        Assert.Equal(unstaged, file.Unstaged);
    }

    [Fact]
    public void Parse_StagedAndUnstagedChanges_ReportsBoth()
    {
        var file = Assert.Single(StatusParser.Parse(Z(Ordinary("MM", "a.txt"))).Files);

        Assert.True(file.HasStagedChanges);
        Assert.True(file.HasUnstagedChanges);
    }

    [Theory]
    [InlineData("with space.txt")]
    [InlineData("папка/файл.cs")]
    [InlineData("dir with spaces/a b c.txt")]
    public void Parse_PathWithSpacesOrCyrillic_ReturnsFullPath(string path)
    {
        var files = StatusParser.Parse(Z(Ordinary(".M", path), $"? {path}")).Files;

        Assert.All(files, f => Assert.Equal(path, f.Path));
    }

    [Fact]
    public void Parse_RenamedFile_ReturnsOriginalPath()
    {
        var output = Z(
            $"2 R. N... 100644 100644 100644 {Blob} {Blob} R100 new name.txt",
            "old name.txt",
            Ordinary(".M", "after.txt"));

        var files = StatusParser.Parse(output).Files;

        Assert.Equal(2, files.Count);
        Assert.Equal("new name.txt", files[0].Path);
        Assert.Equal("old name.txt", files[0].OriginalPath);
        Assert.Equal(ChangeKind.Renamed, files[0].Staged);
        Assert.Equal("after.txt", files[1].Path);
    }

    [Fact]
    public void Parse_UntrackedFile_IsUntrackedAndNotStaged()
    {
        var file = Assert.Single(StatusParser.Parse(Z("? новый.cs")).Files);

        Assert.Equal("новый.cs", file.Path);
        Assert.True(file.IsUntracked);
        Assert.False(file.HasStagedChanges);
    }

    [Fact]
    public void Parse_UnmergedFile_IsConflicted()
    {
        var output = Z($"u UU N... 100644 100644 100644 100644 {Blob} {Blob} {Blob} conflict file.txt");

        var file = Assert.Single(StatusParser.Parse(output).Files);

        Assert.Equal("conflict file.txt", file.Path);
        Assert.True(file.IsConflicted);
    }

    [Fact]
    public void Parse_IgnoredFile_IsSkipped()
    {
        Assert.Empty(StatusParser.Parse(Z("! bin/")).Files);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsCleanStatus()
    {
        var status = StatusParser.Parse("");

        Assert.True(status.IsClean);
        Assert.Null(status.Branch);
    }
}
