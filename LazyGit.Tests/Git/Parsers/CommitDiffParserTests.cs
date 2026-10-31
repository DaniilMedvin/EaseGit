using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Tests.Git.Parsers;

public class CommitDiffParserTests
{
    [Fact]
    public void Parse_MessageAndDiff_SplitsByNul()
    {
        var output = "feat: add a\n\n- explain why\n\n\0\ndiff --git a/a.txt b/a.txt\n--- a/a.txt\n+++ b/a.txt\n@@ -1 +1 @@\n-old\n+new\n";

        var diff = CommitDiffParser.Parse(output);

        Assert.Equal("feat: add a\n\n- explain why", diff.Message);
        Assert.Equal("diff --git a/a.txt b/a.txt", diff.Lines[0].Text);
        Assert.Contains(new DiffLine(DiffLineKind.Added, "new", NewNumber: 1), diff.Lines);
    }

    [Fact]
    public void Parse_NoChanges_ReturnsEmptyDiff()
    {
        var diff = CommitDiffParser.Parse("chore: empty\n\0\n");

        Assert.Equal("chore: empty", diff.Message);
        Assert.Empty(diff.Lines);
    }
}
