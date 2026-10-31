using LazyGit.Git;
using LazyGit.UI;

namespace LazyGit.Tests.UI;

public class GitErrorsTests
{
    private static GitResult Failed(string stderr, string stdout = "") => new("git test", 1, stdout, stderr);

    [Theory]
    [InlineData("error: Your local changes to the following files would be overwritten by checkout:", "Нельзя переключиться")]
    [InlineData("error: Your local changes to the following files would be overwritten by merge:", "Нельзя выполнить merge")]
    [InlineData("error: the branch 'x' is not fully merged.", "коммиты, которых нет в текущей")]
    [InlineData("fatal: a branch named 'x' already exists", "уже существует")]
    [InlineData("fatal: You have not concluded your merge (MERGE_HEAD exists).", "merge не завершён")]
    [InlineData("fatal: refusing to merge unrelated histories", "нет общей истории")]
    [InlineData("error: cannot delete branch 'x' used by worktree at '/tmp/w'", "другом worktree")]
    public void Explain_KnownError_ReturnsFriendlyText(string stderr, string expected)
    {
        Assert.Contains(expected, GitErrors.Explain(Failed(stderr)));
    }

    [Fact]
    public void Explain_MergeConflictInStdout_ExplainsWhatToDo()
    {
        var result = Failed("", "Auto-merging a.txt\nCONFLICT (content): Merge conflict in a.txt\n");

        Assert.StartsWith("Конфликт при merge", GitErrors.Explain(result));
    }

    [Fact]
    public void Explain_UnknownError_ReturnsFirstLine()
    {
        Assert.Equal("fatal: something odd", GitErrors.Explain(Failed("fatal: something odd\nhint: more\n")));
    }

    [Fact]
    public void Explain_EmptyStderr_FallsBackToStdout()
    {
        Assert.Equal("nothing to commit", GitErrors.Explain(Failed("", "nothing to commit\n")));
    }
}
