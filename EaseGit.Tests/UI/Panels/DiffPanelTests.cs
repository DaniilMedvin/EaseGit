using EaseGit.Git.Parsers;
using EaseGit.Models;
using EaseGit.UI.Panels;

namespace EaseGit.Tests.UI.Panels;

public class DiffPanelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);

    private static readonly Commit Commit = new("a1b2c3d4e5f6", "Тест Юзер", "test@example.com", Now.AddHours(-3), "feat: add a");

    private static PreparedDiff Prepare(string message, params string[] diff) =>
        DiffPanel.PrepareCommit(Commit, new CommitDiff(message, DiffParser.Parse(string.Join('\n', diff) + "\n")), Now);

    private static string[] Texts(PreparedDiff prepared, DiffLineKind kind) =>
        prepared.Lines.Where(l => l.Kind == kind).Select(l => l.Text).ToArray();

    [Fact]
    public void PrepareCommit_StartsWithMetaAndMessage()
    {
        var prepared = Prepare("feat: add a\n\n- explain why");

        Assert.Equal("a1b2c3d4e5f6", prepared.Lines[0].Text);
        Assert.StartsWith("Тест Юзер · ", prepared.Lines[1].Text);
        Assert.EndsWith("(3 ч назад)", prepared.Lines[1].Text);
        Assert.Equal(["feat: add a", "", "- explain why"], Texts(prepared, DiffLineKind.Message).Take(3));
    }

    [Fact]
    public void PrepareCommit_SeveralFiles_AddsHeaderWithPathForEach()
    {
        var prepared = Prepare("feat: change files",
            "diff --git a/my file.txt b/my file.txt",
            "--- a/my file.txt\t",
            "+++ b/my file.txt\t",
            "@@ -1 +1 @@",
            "-old",
            "+new",
            "diff --git a/old.cs b/src/new.cs",
            "similarity index 100%",
            "rename from old.cs",
            "rename to src/new.cs",
            "diff --git a/gone.txt b/gone.txt",
            "deleted file mode 100644",
            "--- a/gone.txt",
            "+++ /dev/null",
            "@@ -1 +0,0 @@",
            "-bye",
            "diff --git a/img.png b/img.png",
            "Binary files a/img.png and b/img.png differ");

        Assert.Equal(["my file.txt", "src/new.cs", "gone.txt", "img.png"], Texts(prepared, DiffLineKind.File));
        Assert.Equal(prepared.Lines.Count, prepared.Highlights.Length);
    }

    [Fact]
    public void PrepareCommit_CSharpFile_HighlightsOnlyItsLines()
    {
        var prepared = Prepare("feat: code",
            "diff --git a/A.cs b/A.cs",
            "--- a/A.cs",
            "+++ b/A.cs",
            "@@ -0,0 +1 @@",
            "+public class A { }",
            "diff --git a/a.txt b/a.txt",
            "--- a/a.txt",
            "+++ b/a.txt",
            "@@ -0,0 +1 @@",
            "+public class B { }");

        var added = prepared.Lines.Select((l, i) => (l, i)).Where(x => x.l.Kind == DiffLineKind.Added).ToList();
        Assert.NotNull(prepared.Highlights[added[0].i]);
        Assert.Null(prepared.Highlights[added[1].i]);
    }

    [Fact]
    public void PrepareCommit_NoFiles_ShowsNote()
    {
        var prepared = Prepare("chore: empty");

        Assert.Contains("В коммите нет изменений файлов", prepared.Lines.Select(l => l.Text));
    }

    [Fact]
    public void PrepareCommit_SeveralFiles_ListsStatWithCountsAndSummary()
    {
        var prepared = Prepare("feat: change files",
            "diff --git a/a.txt b/a.txt",
            "--- a/a.txt",
            "+++ b/a.txt",
            "@@ -1,2 +1,3 @@",
            "-old",
            "+new",
            "+more",
            " same",
            "diff --git a/img.png b/img.png",
            "Binary files a/img.png and b/img.png differ");

        var stats = prepared.Lines.Where(l => l.Kind == DiffLineKind.Stat).ToList();
        Assert.Equal(["a.txt", "img.png"], stats.Select(l => l.Text));
        Assert.Equal((2, 1), stats[0].Counts);
        Assert.Null(stats[1].Counts);
        Assert.Contains(prepared.Lines, l => l.Kind == DiffLineKind.Header && l.Text == "2 файла изменено · +2 · -1");
        // Сводка идёт до самих изменений.
        var lines = prepared.Lines.ToList();
        Assert.True(lines.IndexOf(stats[^1]) < lines.FindIndex(l => l.Kind == DiffLineKind.File));
    }

    [Fact]
    public void PrepareCommit_NoFiles_HasNoStat()
    {
        var prepared = Prepare("chore: empty");

        Assert.DoesNotContain(prepared.Lines, l => l.Kind == DiffLineKind.Stat);
    }

    [Theory]
    [InlineData("short", 10, new[] { "short" })]
    [InlineData("", 10, new[] { "" })]
    [InlineData("one two three four", 10, new[] { "one two ", "three four" })]
    [InlineData("averyveryverylongword tail", 10, new[] { "averyveryv", "erylongwor", "d tail" })]
    public void Breaks_WrapAtSpacesOrByWidth(string text, int chunk, string[] expected)
    {
        Assert.Equal(expected, DiffPanel.Breaks(text, chunk).Select(b => text.Substring(b.Start, b.Length)));
    }
}
