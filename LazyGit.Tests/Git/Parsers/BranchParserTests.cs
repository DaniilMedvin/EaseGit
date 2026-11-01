using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git.Parsers;

public class BranchParserTests
{
    private static string Line(string head, string name, string upstream = "", string track = "") =>
        $"{head}\0{name}\0{upstream}\0{track}";

    [Fact]
    public void Parse_CurrentBranch_IsCurrent()
    {
        var branches = BranchParser.Parse(string.Join('\n', Line("*", "main"), Line(" ", "feature/diff")) + "\n");

        Assert.Equal(2, branches.Count);
        Assert.True(branches[0].IsCurrent);
        Assert.Equal("main", branches[0].Name);
        Assert.False(branches[1].IsCurrent);
        Assert.Equal("feature/diff", branches[1].Name);
    }

    [Fact]
    public void Parse_NoUpstream_ReturnsNullUpstream()
    {
        var branch = Assert.Single(BranchParser.Parse(Line(" ", "local-only")));

        Assert.Null(branch.Upstream);
        Assert.Equal(0, branch.Ahead);
        Assert.Equal(0, branch.Behind);
    }

    [Theory]
    [InlineData("", 0, 0)]
    [InlineData("ahead 3", 3, 0)]
    [InlineData("behind 2", 0, 2)]
    [InlineData("ahead 1, behind 5", 1, 5)]
    public void Parse_Track_ReturnsAheadAndBehind(string track, int ahead, int behind)
    {
        var branch = Assert.Single(BranchParser.Parse(Line("*", "main", "origin/main", track)));

        Assert.Equal("origin/main", branch.Upstream);
        Assert.Equal(ahead, branch.Ahead);
        Assert.Equal(behind, branch.Behind);
        Assert.False(branch.IsUpstreamGone);
    }

    [Fact]
    public void Parse_GoneUpstream_IsUpstreamGone()
    {
        var branch = Assert.Single(BranchParser.Parse(Line(" ", "old", "origin/old", "gone")));

        Assert.True(branch.IsUpstreamGone);
    }

    [Theory]
    [InlineData("фича/панель-файлов")]
    [InlineData("fix/имя с пробелом")]
    public void Parse_CyrillicOrSpacesInName_KeepsFullName(string name)
    {
        var branch = Assert.Single(BranchParser.Parse(Line(" ", name, "origin/" + name, "ahead 1") + "\n"));

        Assert.Equal(name, branch.Name);
        Assert.Equal("origin/" + name, branch.Upstream);
        Assert.Equal(1, branch.Ahead);
    }

    [Fact]
    public void Parse_LineWithTooFewFields_IsSkipped()
    {
        var branches = BranchParser.Parse("*\0main\n" + Line(" ", "ok") + "\n");

        Assert.Equal("ok", Assert.Single(branches).Name);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNoBranches()
    {
        Assert.Empty(BranchParser.Parse(""));
    }
}
