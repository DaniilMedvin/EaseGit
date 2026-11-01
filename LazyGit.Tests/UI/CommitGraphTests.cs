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
    public void Build_MergedBranch_HasOwnColorUntilItJoins()
    {
        var rows = CommitGraph.Build([C("m", "a", "f"), C("f", "a"), C("a")]);

        Assert.Equal(0, rows[0][0].Color);
        Assert.Equal(1, rows[0][2].Color);
        Assert.Equal(1, rows[0][1].Color);
        Assert.Equal(1, rows[1][2].Color);
        Assert.Equal(0, rows[2][0].Color);
        Assert.Equal(1, rows[2][2].Color);
    }

    [Fact]
    public void Build_NewBranchInFreedColumn_GetsNewColor()
    {
        // Ветка a–b закончилась, следующая история c–d занимает ту же колонку, но это другая ветка.
        var rows = CommitGraph.Build([C("a", "b"), C("b"), C("c", "d"), C("d")]);

        Assert.Equal(0, rows[1][0].Color);
        Assert.Equal(1, rows[2][0].Color);
        Assert.Equal(1, rows[3][0].Color);
    }

    [Fact]
    public void Build_CurrentBranchConverges_KeepsItsLineAndFirstColor()
    {
        // fix ответвилась от base, main (HEAD) — merge поверх base. Обе дорожки ждут base: продолжается main.
        var main = C("main", "base") with { Refs = [new CommitRef("main", RefKind.Branch, IsCurrent: true)] };
        var rows = CommitGraph.Build([C("fix", "base"), main, C("base")]);

        Assert.Equal(0, rows[1].First(c => c.Symbol == CommitGraph.Commit).Color);
        Assert.Equal(1, rows[0][0].Color);
        Assert.Equal("╰─● ", new string(rows[2].Select(c => c.Symbol).ToArray()));
        Assert.Equal(0, rows[2][2].Color);
    }
}
