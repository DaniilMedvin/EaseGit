using EaseGit.Git;
using EaseGit.Git.Parsers;

namespace EaseGit.Tests.Git.Parsers;

public sealed class FileHistoryParserTests
{
    [Fact]
    public void Parse_Sample_ReadsStatusAndPathsIncludingRename()
    {
        const string output =
            "\u001e" + "e900\u001ft\u001ft@e\u001f2026-11-01T10:00:00+03:00\u001f" + "edit new\u001fp1\0\nM\0New.cs\0" +
            "\u001e" + "4f0b\u001ft\u001ft@e\u001f2026-11-01T09:00:00+03:00\u001frename\u001fp2\0\nR100\0old.cs\0New.cs\0" +
            "\u001e" + "8af5\u001ft\u001ft@e\u001f2026-11-01T08:00:00+03:00\u001f" + "add old\u001f\0\nA\0old.cs\0";

        var revisions = FileHistoryParser.Parse(output);

        Assert.Equal(["edit new", "rename", "add old"], revisions.Select(r => r.Commit.Subject));
        Assert.Equal(('M', "New.cs", (string?)null), (revisions[0].Status, revisions[0].Path, revisions[0].OldPath));
        Assert.Equal(('R', "New.cs", "old.cs"), (revisions[1].Status, revisions[1].Path, revisions[1].OldPath));
        Assert.Empty(revisions[2].Commit.Parents);
    }

    [Fact]
    public async Task GetFileHistory_RealRename_FollowsAndShowsOldFileDiff()
    {
        var repo = new TempRepository();
        await repo.InitializeAsync();
        try
        {
            var service = new GitService(repo.Git, repo.Path);
            repo.Write("old file.cs", "a\nb\n");
            await repo.CommitAsync("add old");
            repo.Write("old file.cs", "a\nb2\n");
            await repo.CommitAsync("edit old");
            await repo.RunAsync("mv", "old file.cs", "Новый.cs");
            await repo.CommitAsync("rename");
            repo.Write("other.txt", "x\n");
            await repo.CommitAsync("unrelated");

            var history = await service.GetFileHistoryAsync("Новый.cs");

            Assert.Equal(["rename", "edit old", "add old"], history.Select(r => r.Commit.Subject));
            Assert.Equal("old file.cs", history[0].OldPath);
            var diff = await service.GetFileRevisionDiffAsync(history[1]);
            Assert.Equal("edit old", diff.Message);
            Assert.Contains(diff.Lines, l => l.Text == "b2");
            Assert.Contains("Новый.cs", await service.GetTrackedFilesAsync());
        }
        finally
        {
            await repo.DisposeAsync();
        }
    }
}
