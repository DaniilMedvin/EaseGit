using EaseGit.Git.Parsers;

namespace EaseGit.Tests.Git.Parsers;

public class ReflogParserTests
{
    private const string Hash1 = "9e94b1da1e1406c4b4f02b1ffab44b697d3b9c7c";
    private const string Hash2 = "96a355cf1028fd3463b9e431a874a002dfa858c4";

    [Fact]
    public void Parse_Entries_ReturnsHashAndSubjectInOrder()
    {
        var output = $"{Hash1}\u001fcommit: feat: add a\0{Hash2}\u001fcheckout: moving from main to feature/x\0";

        var entries = ReflogParser.Parse(output);

        Assert.Equal(2, entries.Count);
        Assert.Equal((Hash1, "commit: feat: add a"), (entries[0].Hash, entries[0].Subject));
        Assert.Equal((Hash2, "checkout: moving from main to feature/x"), (entries[1].Hash, entries[1].Subject));
    }

    [Fact]
    public void Parse_CyrillicSubject_KeepsSubject()
    {
        var entry = Assert.Single(ReflogParser.Parse($"{Hash1}\u001fcommit: feat: панель веток\0"));

        Assert.Equal("commit: feat: панель веток", entry.Subject);
    }

    [Fact]
    public void Parse_LeadingNewlineBetweenRecords_IsTrimmed()
    {
        var entries = ReflogParser.Parse($"{Hash1}\u001fa\0\n{Hash2}\u001fb\0");

        Assert.Equal(Hash2, entries[1].Hash);
    }

    [Fact]
    public void Parse_RecordWithoutSubjectSeparator_IsSkipped()
    {
        Assert.Empty(ReflogParser.Parse($"{Hash1}\0"));
    }

    [Fact]
    public void Parse_EmptyOutput_ReturnsNoEntries()
    {
        Assert.Empty(ReflogParser.Parse(""));
    }
}
