using LazyGit.GitHub;
using LazyGit.Models;

namespace LazyGit.Tests.GitHub;

public sealed class PullRequestParserTests
{
    // Сокращённый реальный вывод gh pr list --json (cli/cli).
    private const string ListJson = """
        [
          {
            "additions": 188, "deletions": 3,
            "author": { "id": "MDQ6", "is_bot": false, "login": "tidy-dev", "name": "tidy-dev" },
            "baseRefName": "trunk", "headRefName": "tidy-dev-prepare-team-pr-skill",
            "isDraft": false, "number": 14846, "reviewDecision": "REVIEW_REQUIRED", "state": "OPEN",
            "statusCheckRollup": [
              { "__typename": "CheckRun", "conclusion": "SUCCESS", "name": "build (ubuntu-latest)", "status": "COMPLETED", "workflowName": "Unit and Integration Tests" },
              { "__typename": "CheckRun", "conclusion": "", "name": "lint", "status": "IN_PROGRESS", "workflowName": "Lint" },
              { "__typename": "StatusContext", "context": "codecov/patch", "state": "SUCCESS" }
            ],
            "title": "Add shared team PR preparation skill",
            "updatedAt": "2026-10-28T02:11:55Z",
            "url": "https://github.com/cli/cli/pull/14846"
          },
          {
            "author": { "login": "me" }, "isDraft": true, "number": 7, "reviewDecision": "",
            "statusCheckRollup": [ { "__typename": "CheckRun", "conclusion": "FAILURE", "name": "test", "status": "COMPLETED" } ],
            "title": "wip", "updatedAt": "2026-10-27T10:00:00Z", "url": "https://github.com/cli/cli/pull/7"
          }
        ]
        """;

    [Fact]
    public void ParseList_RealOutput_ReadsFieldsAndChecks()
    {
        var prs = PullRequestParser.ParseList(ListJson);

        var pr = prs[0];
        Assert.Equal(new GitHubRepo("cli", "cli"), pr.Repo);
        Assert.Equal(14846, pr.Number);
        Assert.Equal("tidy-dev", pr.Author);
        Assert.Equal("tidy-dev-prepare-team-pr-skill", pr.HeadRef);
        Assert.Equal("trunk", pr.BaseRef);
        Assert.Equal(ReviewState.Required, pr.Review);
        Assert.Equal(CheckState.Pending, pr.Checks);
        Assert.Equal("Unit and Integration Tests / build (ubuntu-latest)", pr.CheckRuns[0].Name);
        Assert.Equal(CheckState.Success, pr.CheckRuns[2].State);
        Assert.Equal((188, 3), (pr.Additions, pr.Deletions));

        Assert.True(prs[1].IsDraft);
        Assert.Equal(CheckState.Failure, prs[1].Checks);
        Assert.Equal(ReviewState.None, prs[1].Review);
    }

    [Fact]
    public void ParseList_SearchOutput_ReadsRepoFromUrlWithoutBranches()
    {
        const string json = """
            [{"author":{"login":"tidy-dev","type":"User"},"isDraft":false,"number":14846,
              "repository":{"name":"cli","nameWithOwner":"cli/cli"},"state":"open","title":"Add skill",
              "updatedAt":"2026-10-28T02:11:55Z","url":"https://github.com/cli/cli/pull/14846"}]
            """;

        var pr = Assert.Single(PullRequestParser.ParseList(json));

        Assert.Equal("cli/cli", pr.Repo.NameWithOwner);
        Assert.Null(pr.HeadRef);
        Assert.Equal(CheckState.None, pr.Checks);
    }

    [Fact]
    public void ParseOne_View_ReadsBodyAndMergeable()
    {
        const string json = """
            {"number":3,"title":"t","author":{"login":"a"},"url":"https://github.com/o/r/pull/3","updatedAt":"2026-10-28T02:11:55Z",
             "body":"## What\n- x","mergeable":"CONFLICTING","statusCheckRollup":[]}
            """;

        var pr = PullRequestParser.ParseOne(json);

        Assert.Equal("## What\n- x", pr?.Body);
        Assert.Equal("CONFLICTING", pr?.Mergeable);
    }

    [Fact]
    public void ParseList_Empty_ReturnsEmpty()
    {
        Assert.Empty(PullRequestParser.ParseList("[]"));
    }
}
