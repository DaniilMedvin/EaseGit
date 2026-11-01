using LazyGit.Git.Parsers;

namespace LazyGit.Tests.Git.Parsers;

public class StashParserTests
{
    [Fact]
    public void Parse_OwnMessageAndWip_ExtractsBranchAndMessage()
    {
        var output =
            "stash@{0}\u001f" + "aaa\u001fТест\u001ft@e\u001f2026-11-01T00:19:17+02:00\u001fOn main: моё сообщение\0" +
            "stash@{1}\u001f" + "bbb\u001fТест\u001ft@e\u001f2026-11-01T00:19:17+02:00\u001fWIP on feature/x: 7949d52 init\0";

        var entries = StashParser.Parse(output);

        Assert.Equal(2, entries.Count);
        Assert.Equal(("stash@{0}", 0, "моё сообщение", "main"), (entries[0].Ref, entries[0].Index, entries[0].Message, entries[0].Branch));
        Assert.Equal((1, "WIP on feature/x: 7949d52 init", "feature/x"), (entries[1].Index, entries[1].Message, entries[1].Branch));
    }

    [Fact]
    public void Parse_MessageWithColon_SplitsOnlyOnFirstSeparator()
    {
        var output = "stash@{3}\u001fccc\u001fA\u001fa@e\u001f2026-11-01T00:19:17+02:00\u001fOn main: fix: a: b\0";

        var entry = Assert.Single(StashParser.Parse(output));

        Assert.Equal((3, "fix: a: b", "main"), (entry.Index, entry.Message, entry.Branch));
    }

    [Fact]
    public void Parse_UnknownSubjectFormat_KeepsWholeMessageWithoutBranch()
    {
        var output = "stash@{0}\u001fccc\u001fA\u001fa@e\u001f2026-11-01T00:19:17+02:00\u001fСвободный текст\0";

        var entry = Assert.Single(StashParser.Parse(output));

        Assert.Equal("Свободный текст", entry.Message);
        Assert.Null(entry.Branch);
    }

    [Fact]
    public void Parse_BranchWithCyrillicAndSpaces_KeepsBranch()
    {
        var output = "stash@{0}\u001fccc\u001fA\u001fa@e\u001f2026-11-01T00:19:17+02:00\u001fOn фича/панель: тест\0";

        var entry = Assert.Single(StashParser.Parse(output));

        Assert.Equal("фича/панель", entry.Branch);
    }

    [Fact]
    public void Parse_Empty_ReturnsEmpty()
    {
        Assert.Empty(StashParser.Parse(""));
    }
}
