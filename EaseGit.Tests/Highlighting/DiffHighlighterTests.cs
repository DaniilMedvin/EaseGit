using EaseGit.Highlighting;
using EaseGit.Models;

namespace EaseGit.Tests.Highlighting;

public class DiffHighlighterTests
{
    private static DiffLine Context(string text) => new(DiffLineKind.Context, text, 1, 1);
    private static DiffLine Added(string text) => new(DiffLineKind.Added, text, NewNumber: 1);
    private static DiffLine Removed(string text) => new(DiffLineKind.Removed, text, OldNumber: 1);
    private static DiffLine Hunk() => new(DiffLineKind.Hunk, "@@ -1,2 +1,2 @@");

    private static TokenClass ClassAt(IReadOnlyList<HighlightSpan>? spans, string line, string token)
    {
        var index = line.IndexOf(token, StringComparison.Ordinal);
        Assert.NotNull(spans);
        var span = spans.FirstOrDefault(s => s.Start <= index && index < s.End);
        return span.Length == 0 ? TokenClass.Plain : span.Class;
    }

    [Theory]
    [InlineData("a.cs", true)]
    [InlineData("Script.CSX", true)]
    [InlineData("a.txt", false)]
    [InlineData("Makefile", false)]
    public void IsSupported_ByExtension(string path, bool expected)
    {
        Assert.Equal(expected, DiffHighlighter.IsSupported(path));
    }

    [Fact]
    public void Highlight_UnsupportedLanguage_ReturnsNulls()
    {
        var result = DiffHighlighter.Highlight([Hunk(), Added("x")], "a.txt");

        Assert.All(result, Assert.Null);
    }

    [Fact]
    public void Highlight_ServiceLines_AreNotHighlighted()
    {
        var result = DiffHighlighter.Highlight([Hunk(), Context("var x = 1;")], "a.cs");

        Assert.Null(result[0]);
        Assert.NotNull(result[1]);
    }

    [Fact]
    public void Highlight_AddedAndRemoved_UseTheirOwnVersionOfCode()
    {
        DiffLine[] lines = [Hunk(), Removed("return new AppConfig();"), Added("return AppConfig.Default;")];

        var result = DiffHighlighter.Highlight(lines, "a.cs");

        Assert.Equal(TokenClass.ControlKeyword, ClassAt(result[1], lines[1].Text, "return"));
        Assert.Equal(TokenClass.Keyword, ClassAt(result[1], lines[1].Text, "new"));
        Assert.Equal(TokenClass.Class, ClassAt(result[2], lines[2].Text, "AppConfig"));
    }

    [Fact]
    public void Highlight_MultiLineComment_IsSplitByLines()
    {
        DiffLine[] lines = [Hunk(), Context("/* начало"), Added("   середина"), Context("   конец */ int x;")];

        var result = DiffHighlighter.Highlight(lines, "a.cs");

        Assert.Equal(TokenClass.Comment, ClassAt(result[1], lines[1].Text, "начало"));
        Assert.Equal(TokenClass.Comment, ClassAt(result[2], lines[2].Text, "середина"));
        Assert.Equal(TokenClass.Comment, ClassAt(result[3], lines[3].Text, "конец"));
        Assert.Equal(TokenClass.Keyword, ClassAt(result[3], lines[3].Text, "int"));
    }

    [Fact]
    public void Highlight_SpansStayInsideLine()
    {
        DiffLine[] lines = [Hunk(), Context("class A"), Context("{"), Added("    void M() { }"), Context("}")];

        var result = DiffHighlighter.Highlight(lines, "a.cs");

        for (var i = 1; i < lines.Length; i++)
            Assert.All(result[i]!, s => Assert.InRange(s.End, 0, lines[i].Text.Length));
    }
}
