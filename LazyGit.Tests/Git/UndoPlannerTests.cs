using LazyGit.Git;
using LazyGit.Git.Parsers;
using LazyGit.Models;

namespace LazyGit.Tests.Git;

public class UndoPlannerTests
{
    private static UndoPlan PlanFor(string subject) =>
        UndoPlanner.Plan(new ReflogEntry("abc1234", subject)).Plan ?? throw new Xunit.Sdk.XunitException("план не построен");

    private static string ReasonFor(string subject) => UndoPlanner.Plan(new ReflogEntry("abc1234", subject)).Reason!;

    [Theory]
    [InlineData("commit: feat: add a", "reset --soft HEAD@{1}")]
    [InlineData("commit (amend): feat: add a", "reset --soft HEAD@{1}")]
    [InlineData("merge feature: Fast-forward", "reset --keep HEAD@{1}")]
    [InlineData("pull --no-edit: Merge made by the 'ort' strategy.", "reset --keep HEAD@{1}")]
    [InlineData("commit (merge): Merge branch 'f'", "reset --keep HEAD@{1}")]
    [InlineData("checkout: moving from main to feature/x", "switch --no-guess main")]
    [InlineData("checkout: moving from 1a2b3c4d5e to main", "switch --detach 1a2b3c4d5e")]
    public void Plan_KnownAction_ReturnsCommand(string subject, string expected)
    {
        Assert.Equal(expected, string.Join(' ', PlanFor(subject).Args));
    }

    [Fact]
    public void Plan_Commit_RewritesHistoryAndNamesCommit()
    {
        var plan = PlanFor("commit: feat: add a");

        Assert.True(plan.RewritesHistory);
        Assert.Contains("«feat: add a»", plan.Question);
    }

    [Fact]
    public void Plan_Checkout_DoesNotRewriteHistory()
    {
        Assert.False(PlanFor("checkout: moving from main to f").RewritesHistory);
    }

    [Theory]
    [InlineData("commit (initial): feat: first", "Первый коммит")]
    [InlineData("reset: moving to HEAD@{1}", "уже отмена")]
    [InlineData("rebase (finish): returning to refs/heads/main", "Rebase")]
    [InlineData("something new: ???", "Не знаю")]
    public void Plan_UnsupportedAction_ExplainsWhy(string subject, string expected)
    {
        Assert.Contains(expected, ReasonFor(subject));
    }

    [Fact]
    public void Plan_NoEntry_NothingToUndo()
    {
        Assert.Contains("нечего", UndoPlanner.Plan(null).Reason);
    }

    [Fact]
    public void ReflogParser_ParsesHashAndSubject()
    {
        var entries = ReflogParser.Parse("abc\x1f" + "commit: a\0\ndef\x1f" + "checkout: moving from a to b\0");

        Assert.Equal([new ReflogEntry("abc", "commit: a"), new ReflogEntry("def", "checkout: moving from a to b")], entries);
    }
}
