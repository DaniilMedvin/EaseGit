using LazyGit.Git;
using LazyGit.GitHub;
using LazyGit.Models;
using LazyGit.UI;
using LazyGit.UI.Panels;

namespace LazyGit.Tests.GitHub;

public sealed class GitHubServiceTests
{
    private static readonly PullRequest Pr = new(new GitHubRepo("o", "r"), 12, "feat: x", "anna", "https://github.com/o/r/pull/12",
        DateTimeOffset.Now.AddHours(-2));

    [Fact]
    public void Arguments_AlwaysPassRepoExplicitly()
    {
        Assert.Equal(["pr", "checkout", "12", "--repo", "o/r"], GitHubService.CheckoutArguments(Pr));
        Assert.Equal(["pr", "merge", "12", "--repo", "o/r", "--squash"], GitHubService.MergeArguments(Pr, MergeMethod.Squash));
        Assert.Equal(["pr", "review", "12", "--repo", "o/r", "--approve"], GitHubService.ApproveArguments(Pr));
    }

    [Fact]
    public void CreateArguments_BodyFromStdinAndOptionalDraft()
    {
        var args = GitHubService.CreateArguments(Pr.Repo, "feature/x", "main", "Мой PR", draft: true);

        Assert.Equal(["pr", "create", "--repo", "o/r", "--head", "feature/x", "--base", "main", "--title", "Мой PR", "--body-file", "-", "--draft"], args);
        Assert.DoesNotContain("--draft", GitHubService.CreateArguments(Pr.Repo, "a", "main", "t", draft: false));
    }

    [Fact]
    public void PreparePullRequest_StripsTemplateCommentsAndListsFailedChecksFirst()
    {
        var pr = Pr with
        {
            HeadRef = "feature/x",
            BaseRef = "main",
            Body = "<!-- шаблон -->\n\n\n\n## Что\nсделано",
            Checks = CheckState.Failure,
            Review = ReviewState.Approved,
            CheckRuns = [new PullRequestCheck("build", CheckState.Success), new PullRequestCheck("lint", CheckState.Failure)],
        };

        var prepared = DiffPanel.PreparePullRequest(pr, [], DateTimeOffset.Now);
        var texts = prepared.Lines.Select(l => l.Text).ToList();

        Assert.StartsWith("o/r#12 · feature/x → main · anna", texts[0]);
        Assert.Equal("✗ CI · ✓ одобрен", texts[1]);
        Assert.Equal("feat: x", texts[2]);
        Assert.DoesNotContain(texts, t => t.Contains("<!--"));
        Assert.Equal(["## Что", "сделано"], texts.SkipWhile(t => t != "## Что").Take(2));
        Assert.True(texts.IndexOf("✗ lint") < texts.IndexOf("✓ build"));
    }

    [Fact]
    public void PullRequestState_DraftAndConflict_AreShown()
    {
        var state = DiffPanel.PullRequestState(Pr with { IsDraft = true, Mergeable = "CONFLICTING" });

        Assert.Contains("черновик", state);
        Assert.Contains("конфликт", state);
    }

    [Theory]
    [InlineData("feature/hourly-forecast", "hourly forecast")]
    [InlineData("fix_city_name", "fix city name")]
    public void TitleFromBranch_LastSegmentWithSpaces(string branch, string expected)
    {
        Assert.Equal(expected, MainWindow.TitleFromBranch(branch));
    }

    [Theory]
    [InlineData("pull request create failed: GraphQL: A pull request already exists for o:feature/x.", "уже открыт")]
    [InlineData("failed to create review: GraphQL: Can not approve your own pull request", "Свой PR")]
    [InlineData("X Pull request o/r#12 is not mergeable: the merge commit cannot be cleanly created.", "конфликт")]
    [InlineData("error connecting to api.github.com", "Нет связи")]
    public void Explain_GhErrors_AreFriendly(string stderr, string expected)
    {
        Assert.Contains(expected, GitErrors.Explain(new GitResult("gh pr create", 1, "", stderr)));
    }
}
