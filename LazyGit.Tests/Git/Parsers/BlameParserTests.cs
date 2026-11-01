using LazyGit.Git;
using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git.Parsers;

public sealed class BlameParserTests
{
    private const string Output = """
        8af5ebd70974f990a0f4f10b7210175292db494f 1 1 1
        author Тест Юзер
        author-mail <t@e>
        author-time 1793567328
        author-tz +0300
        summary add old
        boundary
        filename old.cs
        	a
        1fe209aa695464a4d064551f6196dbc248d239c1 2 2 1
        author t
        author-mail <t@e>
        author-time 1793567400
        author-tz -0130
        summary edit old
        previous 8af5ebd70974f990a0f4f10b7210175292db494f old file.cs
        filename old.cs
        	b2
        8af5ebd70974f990a0f4f10b7210175292db494f 3 3 2
        	c
        8af5ebd70974f990a0f4f10b7210175292db494f 4 4
        	d
        0000000000000000000000000000000000000000 5 5 1
        author Not Committed Yet
        author-mail <not.committed.yet>
        author-time 1793570000
        author-tz +0000
        summary Version of New.cs from New.cs
        filename New.cs
        	new line
        """;

    [Fact]
    public void Parse_Porcelain_ReadsLinesAndCommitFieldsOnce()
    {
        var lines = BlameParser.Parse(Output);

        Assert.Equal([1, 2, 3, 4, 5], lines.Select(l => l.Number));
        Assert.Equal(["a", "b2", "c", "d", "new line"], lines.Select(l => l.Text));
        Assert.Equal("Тест Юзер", lines[2].Commit.Author);
        Assert.Equal("add old", lines[3].Commit.Summary);
        Assert.Equal(TimeSpan.FromHours(3), lines[0].Commit.Date.Offset);
        Assert.Equal(TimeSpan.FromMinutes(-90), lines[1].Commit.Date.Offset);
        Assert.Equal(("8af5ebd70974f990a0f4f10b7210175292db494f", "old file.cs"), (lines[1].Commit.PreviousHash, lines[1].Commit.PreviousPath));
        Assert.Equal("old.cs", lines[1].Commit.Path);
        Assert.True(lines[4].Commit.IsUncommitted);
        Assert.False(lines[0].Commit.IsUncommitted);
    }

    [Fact]
    public async Task GetBlame_RealRepoAtRevisionAndWorkingTree()
    {
        var repo = new TempRepository();
        await repo.InitializeAsync();
        try
        {
            var service = new GitService(repo.Git, repo.Path);
            repo.Write("old.cs", "a\nb\n");
            await repo.CommitAsync("add");
            repo.Write("old.cs", "a\nb2\n");
            await repo.CommitAsync("edit");
            await repo.RunAsync("mv", "old.cs", "Новый файл.cs");
            await repo.CommitAsync("rename");
            repo.Write("Новый файл.cs", "a\nb2\nc\n");

            var now = await service.GetBlameAsync("Новый файл.cs");
            Assert.Equal(["add", "edit"], now.Take(2).Select(l => l.Commit.Summary));
            Assert.Equal("old.cs", now[0].Commit.Path);
            Assert.True(now[2].Commit.IsUncommitted);

            var before = await service.GetBlameAsync(now[1].Commit.PreviousPath!, now[1].Commit.PreviousHash);
            Assert.Equal(["a", "b"], before.Select(l => l.Text));
        }
        finally
        {
            await repo.DisposeAsync();
        }
    }
}
