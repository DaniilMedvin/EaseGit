using EaseGit.GitHub;

namespace EaseGit.Tests.GitHub;

public sealed class IssueParserTests
{
    [Fact]
    public void ParseList_RealShape()
    {
        const string json = """
            [{"assignees":[{"login":"anna"}],"author":{"login":"Izzur","name":"Izzur Zuhri"},
              "comments":[{"author":{"login":"x"},"body":"b","createdAt":"2026-11-01T09:26:21Z"}],
              "labels":[{"name":"needs-triage","color":"D6393F"},{"name":"bug"}],"number":14895,"state":"OPEN",
              "title":"gh skill: lock file ignores XDG_STATE_HOME","updatedAt":"2026-11-01T09:26:21Z","url":"https://github.com/cli/cli/issues/14895"}]
            """;

        var issue = Assert.Single(IssueParser.ParseList(json));

        Assert.Equal(("cli/cli", 14895, "Izzur"), (issue.Repo.NameWithOwner, issue.Number, issue.Author));
        Assert.Equal(["needs-triage", "bug"], issue.Labels);
        Assert.Equal(["anna"], issue.Assignees);
        Assert.Equal(1, issue.CommentCount);
    }

    [Fact]
    public void ParseOne_BodyAndComments()
    {
        const string json = """
            {"number":3,"title":"t","author":{"login":"a"},"url":"https://github.com/o/r/issues/3","updatedAt":"2026-11-01T09:00:00Z",
             "createdAt":"2026-10-30T09:00:00Z","state":"OPEN","body":"### Bug\n- steps","labels":[],"assignees":[],
             "comments":[{"author":{"login":"cli-triage"},"body":"Thanks!","createdAt":"2026-11-01T10:00:00Z"}]}
            """;

        var issue = IssueParser.ParseOne(json)!;

        Assert.Equal("### Bug\n- steps", issue.Body);
        Assert.Equal(30, issue.CreatedAt?.Day);
        var comment = Assert.Single(issue.Comments);
        Assert.Equal(("cli-triage", "Thanks!"), (comment.Author, comment.Body));
    }
}
