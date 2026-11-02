using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

public sealed class ConflictResolverTests
{
    private const string Text =
        "using System;\r\n" +
        "<<<<<<< HEAD\r\n" +
        "var a = 1;\r\n" +
        "=======\r\n" +
        "var a = 2;\r\n" +
        ">>>>>>> feature/x\r\n" +
        "middle\r\n" +
        "<<<<<<< HEAD\r\n" +
        "ours\r\n" +
        "||||||| base\r\n" +
        "base\r\n" +
        "=======\r\n" +
        "theirs\r\n" +
        ">>>>>>> feature/x\r\n";

    [Fact]
    public void Parse_TwoBlocksWithDiff3_KeepsLinesAndLabels()
    {
        var parts = ConflictResolver.Parse(Text)!;

        Assert.Equal(2, ConflictResolver.CountBlocks(parts));
        var first = (ConflictBlock)parts[1];
        Assert.Equal("HEAD", first.OursLabel);
        Assert.Equal("feature/x", first.TheirsLabel);
        Assert.Equal(["var a = 1;\r\n"], first.Ours);
        Assert.Null(first.Base);
        var second = (ConflictBlock)parts[3];
        Assert.Equal(["base\r\n"], second.Base);
    }

    [Fact]
    public void Render_Unresolved_IsSameText()
    {
        Assert.Equal(Text, ConflictResolver.Render(ConflictResolver.Parse(Text)!));
    }

    [Theory]
    [InlineData(ConflictChoice.Ours, "var a = 1;\r\n")]
    [InlineData(ConflictChoice.Theirs, "var a = 2;\r\n")]
    [InlineData(ConflictChoice.Both, "var a = 1;\r\nvar a = 2;\r\n")]
    public void Resolve_FirstBlock_ReplacesOnlyItKeepingCrlf(ConflictChoice choice, string kept)
    {
        var parts = ConflictResolver.Resolve(ConflictResolver.Parse(Text)!, 0, choice);

        var text = ConflictResolver.Render(parts);
        Assert.StartsWith("using System;\r\n" + kept + "middle\r\n<<<<<<< HEAD\r\n", text);
        Assert.Equal(1, ConflictResolver.CountBlocks(ConflictResolver.Parse(text)!));
    }

    [Fact]
    public void Resolve_BlockAtEndOfFileWithoutNewline_KeepsEnding()
    {
        var parts = ConflictResolver.Parse("x\n<<<<<<< HEAD\nours\n=======\ntheirs\n>>>>>>> b")!;

        Assert.Equal("x\nours\ntheirs\n", ConflictResolver.Render(ConflictResolver.Resolve(parts, 0, ConflictChoice.Both)));
        Assert.Equal("x\ntheirs\n", ConflictResolver.Render(ConflictResolver.Resolve(parts, 0, ConflictChoice.Theirs)));
    }

    [Theory]
    [InlineData("<<<<<<< HEAD\nours\n=======\ntheirs\n")]
    [InlineData("<<<<<<< HEAD\na\n=======\nb\n=======\nc\n>>>>>>> x\n")]
    [InlineData("<<<<<<< HEAD\n<<<<<<< nested\n=======\n>>>>>>> x\n")]
    public void Parse_BrokenMarkers_ReturnsNull(string text)
    {
        Assert.Null(ConflictResolver.Parse(text));
    }

    [Fact]
    public void Parse_LookalikeLines_AreText()
    {
        var parts = ConflictResolver.Parse("========\n<<<<<<<<< not a marker\n=======x\n")!;

        Assert.Equal(0, ConflictResolver.CountBlocks(parts));
    }
}
