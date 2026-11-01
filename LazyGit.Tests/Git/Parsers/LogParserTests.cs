using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Tests.Git.Parsers;

public class LogParserTests
{
    private const string Hash1 = "9e94b1da1e1406c4b4f02b1ffab44b697d3b9c7c";
    private const string Hash2 = "96a355cf1028fd3463b9e431a874a002dfa858c4";

    private static string Record(string hash, string subject, string date = "2026-10-31T21:15:01+02:00") =>
        string.Join('\x1f', hash, "Тест Юзер", "test@example.com", date, subject);

    [Fact]
    public void Parse_Commits_ReturnsFieldsInOrder()
    {
        var output = Record(Hash1, "fix: second") + "\0" + Record(Hash2, "init: первый коммит") + "\0";

        var commits = LogParser.Parse(output);

        Assert.Equal(2, commits.Count);
        Assert.Equal(Hash1, commits[0].Hash);
        Assert.Equal("9e94b1d", commits[0].ShortHash);
        Assert.Equal("Тест Юзер", commits[0].AuthorName);
        Assert.Equal("test@example.com", commits[0].AuthorEmail);
        Assert.Equal("fix: second", commits[0].Subject);
        Assert.Equal("init: первый коммит", commits[1].Subject);
    }

    [Fact]
    public void Parse_Date_KeepsTimeZoneOffset()
    {
        var commit = Assert.Single(LogParser.Parse(Record(Hash1, "x", "2026-10-31T21:15:01+02:00")));

        Assert.Equal(new DateTimeOffset(2026, 10, 31, 21, 15, 1, TimeSpan.FromHours(2)), commit.Date);
    }

    [Theory]
    [InlineData("fix: subject | with pipe")]
    [InlineData("feat: «кавычки», запятые; и: двоеточия")]
    [InlineData("")]
    public void Parse_SpecialCharactersInSubject_KeepsSubject(string subject)
    {
        var commit = Assert.Single(LogParser.Parse(Record(Hash1, subject)));

        Assert.Equal(subject, commit.Subject);
    }

    [Fact]
    public void Parse_NewlineBetweenRecords_DoesNotBreakHash()
    {
        var output = Record(Hash1, "a") + "\0\n" + Record(Hash2, "b") + "\0";

        var commits = LogParser.Parse(output);

        Assert.Equal(Hash2, commits[1].Hash);
    }

    [Fact]
    public void Parse_IncompleteRecord_IsSkipped()
    {
        var commits = LogParser.Parse("abc\u001fname\0" + Record(Hash1, "ok") + "\0");

        Assert.Equal(Hash1, Assert.Single(commits).Hash);
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNoCommits()
    {
        Assert.Empty(LogParser.Parse(""));
    }

    [Fact]
    public void Parse_ParentsAndRefs_AreRead()
    {
        var record = Record(Hash1, "merge") + "\x1f" + Hash2 + " " + Hash1 + "\x1f"
            + "HEAD -> refs/heads/feature/x, tag: refs/tags/v1.0, refs/remotes/origin/feature/x, refs/remotes/origin/HEAD";

        var commit = Assert.Single(LogParser.Parse(record));

        Assert.Equal([Hash2, Hash1], commit.Parents);
        Assert.Equal(
        [
            new CommitRef("feature/x", RefKind.Branch, IsCurrent: true),
            new CommitRef("v1.0", RefKind.Tag),
            new CommitRef("origin/feature/x", RefKind.Remote),
        ], commit.Refs);
    }

    [Fact]
    public void ParseRefs_DetachedHead_ReturnsHead()
    {
        Assert.Equal([new CommitRef("HEAD", RefKind.Head)], LogParser.ParseRefs("HEAD"));
    }

}
