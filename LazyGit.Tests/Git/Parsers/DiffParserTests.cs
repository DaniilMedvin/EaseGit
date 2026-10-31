using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Tests.Git.Parsers;

public class DiffParserTests
{
    private static string Lines(params string[] lines) => string.Join('\n', lines) + "\n";

    [Fact]
    public void Parse_Modification_ReturnsLineNumbers()
    {
        var diff = DiffParser.Parse(Lines(
            "diff --git a/a.txt b/a.txt",
            "index 7898192..9ad2ebb 100644",
            "--- a/a.txt",
            "+++ b/a.txt",
            "@@ -10,3 +10,3 @@ class A",
            " context",
            "-old",
            "+new",
            " after"));

        var body = diff.Where(l => l.Kind is DiffLineKind.Context or DiffLineKind.Added or DiffLineKind.Removed).ToList();
        Assert.Equal(
        [
            new DiffLine(DiffLineKind.Context, "context", 10, 10),
            new DiffLine(DiffLineKind.Removed, "old", OldNumber: 11),
            new DiffLine(DiffLineKind.Added, "new", NewNumber: 11),
            new DiffLine(DiffLineKind.Context, "after", 12, 12),
        ], body);
        Assert.Equal(4, diff.Count(l => l.Kind == DiffLineKind.Header));
        Assert.Single(diff, l => l.Kind == DiffLineKind.Hunk);
    }

    [Fact]
    public void Parse_HeaderLinesStartingWithPlusOrMinus_AreHeadersNotChanges()
    {
        var diff = DiffParser.Parse(Lines("diff --git a/a b/a", "--- a/a", "+++ b/a"));

        Assert.All(diff, l => Assert.Equal(DiffLineKind.Header, l.Kind));
    }

    [Fact]
    public void Parse_NewFile_StartsFromLineOne()
    {
        var diff = DiffParser.Parse(Lines(
            "diff --git a/n.cs b/n.cs",
            "new file mode 100644",
            "--- /dev/null",
            "+++ b/n.cs",
            "@@ -0,0 +1,2 @@",
            "+namespace X;",
            "+"));

        var added = diff.Where(l => l.Kind == DiffLineKind.Added).ToList();
        Assert.Equal([1, 2], added.Select(l => l.NewNumber!.Value));
        Assert.Equal(["namespace X;", ""], added.Select(l => l.Text));
    }

    [Fact]
    public void Parse_MultipleHunks_ResetsLineNumbers()
    {
        var diff = DiffParser.Parse(Lines(
            "@@ -1,1 +1,1 @@",
            "-a",
            "+b",
            "@@ -50,1 +50,1 @@",
            "-c",
            "+d"));

        Assert.Equal([1, 50], diff.Where(l => l.Kind == DiffLineKind.Removed).Select(l => l.OldNumber!.Value));
    }

    [Fact]
    public void Parse_NoNewlineMarker_IsRecognized()
    {
        var diff = DiffParser.Parse(Lines("@@ -1 +1 @@", "-a", "\\ No newline at end of file", "+a"));

        Assert.Equal(DiffLineKind.NoNewline, diff[2].Kind);
        Assert.Equal(DiffLineKind.Added, diff[3].Kind);
    }

    [Fact]
    public void Parse_BinaryFile_ReturnsBinaryLine()
    {
        var diff = DiffParser.Parse(Lines(
            "diff --git a/b.bin b/b.bin",
            "new file mode 100644",
            "index 0000000..8352675",
            "Binary files /dev/null and b/b.bin differ"));

        Assert.Equal(DiffLineKind.Binary, diff[^1].Kind);
    }

    [Fact]
    public void Parse_CombinedDiff_UsesTwoCharacterPrefix()
    {
        var diff = DiffParser.Parse(Lines(
            "diff --cc f",
            "@@@ -1,1 -1,1 +1,5 @@@",
            "++<<<<<<< HEAD",
            " +3",
            "  same"));

        Assert.Equal(DiffLineKind.Added, diff[2].Kind);
        Assert.Equal("<<<<<<< HEAD", diff[2].Text);
        Assert.Equal("3", diff[3].Text);
        Assert.Equal(new DiffLine(DiffLineKind.Context, "same", 1, 3), diff[4]);
    }

    [Fact]
    public void Parse_WindowsLineEndings_AreTrimmed()
    {
        var diff = DiffParser.Parse("@@ -1 +1 @@\r\n-a\r\n+b\r\n");

        Assert.Equal(["a", "b"], diff.Skip(1).Select(l => l.Text));
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNoLines()
    {
        Assert.Empty(DiffParser.Parse(""));
    }
}
