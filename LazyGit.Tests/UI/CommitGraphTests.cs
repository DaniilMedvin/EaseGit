using LazyGit.Models;
using LazyGit.UI.Panels;

namespace LazyGit.Tests.UI;

public class CommitGraphTests
{
    private static Commit C(string hash, params string[] parents) =>
        new(hash, "a", "a@a", DateTimeOffset.UnixEpoch, hash) { Parents = parents };

    private static List<string> Draw(params Commit[] commits) =>
        CommitGraph.Build(commits).Select(row => new string(row.Select(c => c.Symbol).ToArray())).ToList();

    [Fact]
    public void Build_LinearHistory_OneColumn()
    {
        Assert.Equal(["● ", "● ", "● "], Draw(C("c", "b"), C("b", "a"), C("a")));
    }

    [Fact]
    public void Build_MergeOfBranch_OpensAndClosesSecondLane()
    {
        // m = merge(a2, f); f ответвилась от a1.
        var rows = Draw(C("m", "a2", "f"), C("f", "a1"), C("a2", "a1"), C("a1"));

        Assert.Equal(["◉─╮ ", "│ ● ", "● │ ", "●─╯ "], rows);
    }

    [Fact]
    public void Build_TwoBranchesFromSameCommit_JoinAtParent()
    {
        // Две вершины (как при --all): обе ждут общего родителя.
        var rows = Draw(C("x", "base"), C("y", "base"), C("base"));

        Assert.Equal(["● ", "│ ● ", "●─╯ "], rows);
    }

    [Fact]
    public void Build_MergeIntoExistingLane_UsesTee()
    {
        // m2 сливает b, которую уже ждёт дорожка справа от m2.
        var rows = Draw(C("t", "m2", "b"), C("m2", "a", "b"), C("b", "a"), C("a"));

        Assert.Equal("◉─┤ ", rows[1]);
    }

    [Fact]
    public void Build_LanesHaveColorIndexes()
    {
        var row = CommitGraph.Build([C("m", "a", "f"), C("f", "a"), C("a")])[0];

        Assert.Equal(0, row[0].Lane);
        Assert.Equal(1, row[2].Lane);
        Assert.Equal(1, row[1].Lane);
    }
}
