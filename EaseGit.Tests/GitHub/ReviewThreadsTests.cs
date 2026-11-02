using System.Text.Json;
using EaseGit.Git.Parsers;
using EaseGit.GitHub;
using EaseGit.Models;
using EaseGit.UI.Panels;

namespace EaseGit.Tests.GitHub;

public sealed class ReviewThreadsTests
{
    private static readonly PullRequest Pr = new(new GitHubRepo("o", "r"), 12, "feat: x", "anna", "https://github.com/o/r/pull/12",
        DateTimeOffset.Now.AddHours(-2));

    // Ответ gh api graphql по cli/cli#14752 (тексты укорочены) плюс устаревшее обсуждение к старой версии.
    private const string Sample = """
        {"data":{"repository":{"pullRequest":{"headRefOid":"a3fd228078e85d77a797589d9bb653ec998096c1","reviewThreads":{"nodes":[
          {"id":"PRRT_kwDODKw3uc6t-QaS","isResolved":true,"isOutdated":false,"path":"internal/debug/config_enabled_test.go",
           "line":1,"originalLine":1,"diffSide":"RIGHT",
           "comments":{"nodes":[
             {"author":{"login":"copilot-pull-request-reviewer"},"body":"🛑 Requirement: Exercise the debug-only tests in CI","createdAt":"2026-10-19T14:53:26Z"},
             {"author":{"login":"babakks"},"body":"Not changing this one.","createdAt":"2026-10-19T15:00:08Z"}]}},
          {"id":"PRRT_kwDODKw3uc6t-gef","isResolved":false,"isOutdated":false,"path":"Makefile","line":22,"originalLine":24,"diffSide":"RIGHT",
           "comments":{"nodes":[
             {"author":{"login":"williammartin"},"body":"**Change Requested**\r\n\r\nA plain `make` needs to restore","createdAt":"2026-10-19T15:03:07Z"}]}},
          {"id":"PRRT_old","isResolved":false,"isOutdated":true,"path":"Makefile","line":null,"originalLine":7,"diffSide":"LEFT",
           "comments":{"nodes":[{"author":null,"body":"was here","createdAt":"2026-10-18T10:00:00Z"}]}}
        ]}}}}}
        """;

    private const string MakefileDiff = """
        diff --git a/Makefile b/Makefile
        index 1111111..2222222 100644
        --- a/Makefile
        +++ b/Makefile
        @@ -21,2 +21,2 @@ build:
         	go build
        -	go test
        +	script/build
        """;

    [Fact]
    public void Parse_RealResponse_ReadsThreadsSidesAndComments()
    {
        var result = ReviewThreadParser.Parse(Sample);

        Assert.Equal("a3fd228078e85d77a797589d9bb653ec998096c1", result.HeadSha);
        Assert.Equal(3, result.Threads.Count);
        var resolved = result.Threads[0];
        Assert.True(resolved.IsResolved);
        Assert.Equal(ReviewSide.Right, resolved.Side);
        Assert.Equal(["copilot-pull-request-reviewer", "babakks"], resolved.Comments.Select(c => c.Author));
        Assert.Equal(22, result.Threads[1].Line);
        var outdated = result.Threads[2];
        Assert.Null(outdated.Line);
        Assert.Equal(7, outdated.OriginalLine);
        Assert.Equal(ReviewSide.Left, outdated.Side);
        Assert.Equal("ghost", outdated.Comments[0].Author);
    }

    [Fact]
    public void Parse_PullRequestNotFound_ReturnsEmpty()
    {
        var result = ReviewThreadParser.Parse("""{"data":{"repository":{"pullRequest":null}}}""");

        Assert.Empty(result.Threads);
        Assert.Equal("", result.HeadSha);
    }

    [Fact]
    public void PreparePullRequest_ThreadGoesUnderItsLineAndOutdatedUnderFileHeader()
    {
        var threads = ReviewThreadParser.Parse(Sample).Threads;
        var diff = DiffParser.Parse(MakefileDiff.ReplaceLineEndings("\n") + "\n");

        var lines = DiffPanel.PreparePullRequest(Pr, diff, DateTimeOffset.Now, threads).Lines;

        Assert.Contains(lines, l => l.Text.StartsWith("Обсуждения: 2 открыто · 1 решено"));
        var file = lines.ToList().FindIndex(l => l.Kind == DiffLineKind.File && l.Text == "Makefile");
        // Устаревшее — сразу под заголовком файла, с пометкой о старой строке.
        Assert.Equal(DiffLineKind.Comment, lines[file + 1].Kind);
        Assert.True(lines[file + 1].ThreadStart);
        Assert.Contains("к старой версии, стр. 7", lines[file + 1].Text);
        // Обсуждение строки 22 новой версии — под «+ script/build»: автор, затем текст без пустых строк подряд.
        var added = lines.ToList().FindIndex(l => l.Kind == DiffLineKind.Added && l.NewNumber == 22);
        Assert.StartsWith("williammartin · ", lines[added + 1].Text);
        Assert.True(lines[added + 1].CommentAuthor);
        Assert.Equal(["**Change Requested**", "", "A plain `make` needs to restore"], lines.Skip(added + 2).Take(3).Select(l => l.Text));
        Assert.All(lines.Skip(added + 1).Take(4), l => Assert.Equal("PRRT_kwDODKw3uc6t-gef", l.Thread?.Id));
    }

    [Fact]
    public void ThreadLines_Resolved_CollapsesToOneLineWithFirstComment()
    {
        var resolved = ReviewThreadParser.Parse(Sample).Threads[0];

        var lines = DiffPanel.ThreadLines(resolved, DateTimeOffset.Now, anchored: true).ToList();

        var line = Assert.Single(lines);
        Assert.True(line.ThreadStart);
        Assert.Equal("✓ решено · copilot-pull-request-reviewer: 🛑 Requirement: Exercise the debug-only tests in CI (+1)", line.Text);
    }

    [Fact]
    public void ThreadAnchor_LeftSide_MatchesRemovedLineByOldNumber()
    {
        var thread = new ReviewThread("T", "Makefile", 22, 22, ReviewSide.Left, false, false,
            [new ReviewComment("anna", DateTimeOffset.Now, "почему убрали?")]);
        var diff = DiffParser.Parse(MakefileDiff.ReplaceLineEndings("\n") + "\n");

        var lines = DiffPanel.PreparePullRequest(Pr, diff, DateTimeOffset.Now, [thread]).Lines.ToList();

        var removed = lines.FindIndex(l => l.Kind == DiffLineKind.Removed);
        Assert.Equal("T", lines[removed + 1].Thread?.Id);
    }

    [Fact]
    public void Bodies_CarryUserTextAsJsonThroughStdin()
    {
        Assert.Equal(["api", "graphql", "--input", "-"], GitHubService.GraphQlArguments);
        Assert.Equal(["api", "--method", "POST", "repos/o/r/pulls/12/comments", "--input", "-"], GitHubService.CommentLineArguments(Pr));

        using var comment = JsonDocument.Parse(GitHubService.CommentLineBody("abc", "src/A.cs", 10, ReviewSide.Left, "кавычки \" и\nстроки"));
        Assert.Equal("кавычки \" и\nстроки", comment.RootElement.GetProperty("body").GetString());
        Assert.Equal("abc", comment.RootElement.GetProperty("commit_id").GetString());
        Assert.Equal(10, comment.RootElement.GetProperty("line").GetInt32());
        Assert.Equal("LEFT", comment.RootElement.GetProperty("side").GetString());

        using var graphQl = JsonDocument.Parse(GitHubService.GraphQlBody("mutation { x }", new Dictionary<string, string> { ["thread"] = "T1" }));
        Assert.Equal("T1", graphQl.RootElement.GetProperty("variables").GetProperty("thread").GetString());
    }
}
