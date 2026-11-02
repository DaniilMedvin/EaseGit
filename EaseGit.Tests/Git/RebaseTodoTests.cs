using EaseGit.Git;
using EaseGit.Models;

namespace EaseGit.Tests.Git;

public sealed class RebaseTodoTests
{
    private static Commit C(string hash, string subject = "s") => new(hash, "a", "a@b", DateTimeOffset.Now, subject);

    [Fact]
    public void Build_StepsNewestFirst_WritesOldestFirst()
    {
        List<RebaseStep> steps = [new(C("ccc"), RebaseAction.Squash), new(C("bbb"), RebaseAction.Drop), new(C("aaa"), RebaseAction.Pick)];

        var todo = RebaseTodo.Build(steps, _ => "");

        Assert.Equal("pick aaa\ndrop bbb\nsquash ccc\n", todo);
    }

    [Fact]
    public void Build_Reword_PicksAndAmendsMessageFromQuotedFile()
    {
        List<RebaseStep> steps = [new(C("aaa"), RebaseAction.Reword, "new")];

        var todo = RebaseTodo.Build(steps, _ => "/tmp/it's here/msg");

        Assert.Equal("pick aaa\nexec git commit --amend --only --cleanup=strip --file='/tmp/it'\\''s here/msg'\n", todo);
    }

    [Fact]
    public void Validate_SquashOnOldestKept_ReturnsError()
    {
        List<RebaseStep> steps = [new(C("bbbbbbbb"), RebaseAction.Pick), new(C("aaaaaaaa"), RebaseAction.Fixup)];

        Assert.Contains("aaaaaaa", RebaseTodo.Validate(steps));
    }

    [Fact]
    public void Validate_SquashAboveDroppedOldest_ReturnsError()
    {
        List<RebaseStep> steps = [new(C("cccccccc"), RebaseAction.Squash), new(C("bbbbbbbb"), RebaseAction.Drop)];

        Assert.NotNull(RebaseTodo.Validate(steps));
    }

    [Fact]
    public void Validate_AllDropped_ReturnsError()
    {
        Assert.NotNull(RebaseTodo.Validate([new(C("a"), RebaseAction.Drop)]));
    }

    [Fact]
    public void Validate_SquashIntoOlderPick_Passes()
    {
        List<RebaseStep> steps = [new(C("b"), RebaseAction.Squash), new(C("a"), RebaseAction.Pick)];

        Assert.Null(RebaseTodo.Validate(steps));
    }

    [Fact]
    public void Summary_ActionsAndReorder_ListsAll()
    {
        Commit a = C("a"), b = C("b"), c = C("c");
        List<RebaseStep> steps = [new(a, RebaseAction.Drop), new(c, RebaseAction.Squash), new(b, RebaseAction.Pick)];

        Assert.Equal("1 squash, 1 drop, порядок изменён", RebaseTodo.Summary(steps, [c, b, a]));
        Assert.True(RebaseTodo.HasChanges(steps, [c, b, a]));
    }

    [Fact]
    public void RewordMessage_EmptyBody_SubjectOnly()
    {
        Assert.Equal("feat: x\n", RebaseTodo.RewordMessage(" feat: x ", "\n"));
        Assert.Equal("feat: x\n\nbody\n", RebaseTodo.RewordMessage("feat: x", "body\n\n"));
    }

    [Fact]
    public void Oldest_Reordered_FindsCommitWhoseParentIsOutsideRange()
    {
        var a = C("a") with { Parents = ["base"] };
        var b = C("b") with { Parents = ["a"] };
        List<RebaseStep> steps = [new(a, RebaseAction.Pick), new(b, RebaseAction.Pick)];

        Assert.Same(a, RebaseTodo.Oldest(steps));
    }
}
