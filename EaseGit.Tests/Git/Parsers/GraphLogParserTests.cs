using EaseGit.Git;
using EaseGit.Git.Parsers;
using EaseGit.Models;

namespace EaseGit.Tests.Git.Parsers;

public sealed class GraphLogParserTests
{
    private const char M = '\x1f';

    [Fact]
    public void Parse_CommitWithBody_SplitsGraphAndContent()
    {
        var output = string.Join('\n',
            $"* {M}Ca1b2c3d{M}p1{M}HEAD -> refs/heads/main, refs/remotes/origin/main, tag: refs/tags/v1{M}feat: add a",
            $"| {M}AТест Юзер{M}2026-10-31T09:00:00+00:00",
            "| ",
            "| - | starts like a graph",
            "| ",
            "| ",
            $"* {M}Cb2c3d4e{M}{M}{M}chore: init",
            $"  {M}AТест Юзер{M}2026-10-30T09:00:00+00:00",
            "  ",
            "");

        var lines = GraphLogParser.Parse(output);

        Assert.Equal(
            [GraphLogLineKind.Commit, GraphLogLineKind.Author, GraphLogLineKind.Graph, GraphLogLineKind.Body,
             GraphLogLineKind.Graph, GraphLogLineKind.Commit, GraphLogLineKind.Author],
            lines.Select(l => l.Kind));
        var head = lines[0];
        Assert.Equal("* ", head.Graph);
        Assert.Equal("a1b2c3d", head.ShortHash);
        Assert.Equal("feat: add a", head.Text);
        Assert.False(head.IsMerge);
        Assert.Equal([new CommitRef("main", RefKind.Branch, true), new CommitRef("origin/main", RefKind.Remote), new CommitRef("v1", RefKind.Tag)], head.Refs);
        Assert.Equal("Тест Юзер", lines[1].Text);
        Assert.Equal(2026, lines[1].Date.Year);
        // Текст тела, начинающийся с «-» и «|», не принят за граф.
        Assert.Equal("- | starts like a graph", lines[3].Text);
    }

    [Fact]
    public void Parse_MergeConnector_KeepsGraphOnlyLines()
    {
        var output = string.Join('\n',
            $"*   {M}Cmmmmmmm{M}p1 p2{M}{M}Merge branch 'x'",
            $"|\\  {M}AA{M}2026-10-31T09:00:00+00:00",
            "| | ",
            $"| * {M}Cxxxxxxx{M}p1{M}{M}feat: x",
            $"|/  {M}AA{M}2026-10-31T08:00:00+00:00",
            "|   ",
            $"* {M}Cbbbbbbb{M}{M}{M}base",
            $"  {M}AA{M}2026-10-31T07:00:00+00:00");

        var lines = GraphLogParser.Parse(output);

        Assert.True(lines[0].IsMerge);
        Assert.Equal("|\\  ", lines[1].Graph);
        Assert.Equal("| * ", lines.Single(l => l.ShortHash == "xxxxxxx").Graph);
        Assert.Equal(3, lines.Count(l => l.Kind == GraphLogLineKind.Commit));
    }

    [Fact]
    public async Task Parse_RealGitOutput_ReadsMergeAndBody()
    {
        var repo = new TempRepository();
        await repo.InitializeAsync();
        try
        {
            repo.Write("a.txt", "a\n");
            await repo.CommitAsync("base");
            await repo.RunAsync("switch", "-c", "side");
            repo.Write("b.txt", "b\n");
            await repo.RunAsync("add", "b.txt");
            await repo.RunAsync("commit", "-m", "feat: side", "-m", "Тело | с чертой");
            await repo.RunAsync("switch", "main");
            repo.Write("c.txt", "c\n");
            await repo.CommitAsync("feat: main");
            await repo.RunAsync("merge", "--no-ff", "--no-edit", "side");

            var output = (await repo.RunAsync(GraphLogParser.Arguments("main", 50))).StdOut;
            var lines = GraphLogParser.Parse(output);

            var commits = lines.Where(l => l.Kind == GraphLogLineKind.Commit).ToList();
            Assert.Equal(4, commits.Count);
            Assert.True(commits[0].IsMerge);
            Assert.Contains(commits[0].Refs, r => r is { Name: "main", IsCurrent: true });
            Assert.Contains(lines, l => l.Kind == GraphLogLineKind.Body && l.Text == "Тело | с чертой");
            Assert.All(lines, l => Assert.DoesNotContain('\x1f', l.Graph + l.Text));
        }
        finally
        {
            await repo.DisposeAsync();
        }
    }
}
