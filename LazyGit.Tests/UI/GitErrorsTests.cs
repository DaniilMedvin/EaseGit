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
    [InlineData(" ! [rejected]        main -> main (fetch first)", "Push отклонён")]
    [InlineData(" ! [remote rejected] main -> main (protected branch hook declined)", "Сервер отклонил push")]
    [InlineData("fatal: Need to specify how to reconcile divergent branches.", "разошлись")]
    [InlineData("There is no tracking information for the current branch.", "нет upstream")]
    [InlineData("fatal: could not read Username for 'https://github.com': terminal prompts disabled", "Нет доступа")]
    [InlineData("git@github.com: Permission denied (publickey).", "Нет доступа")]
    [InlineData("fatal: unable to access 'https://x/': Could not resolve host: x", "Не удалось подключиться")]
    [InlineData("fatal: No configured push destination.", "Remote не настроен")]
    public void Explain_KnownError_ReturnsFriendlyText(string stderr, string expected)
    {
        Assert.Contains(expected, GitErrors.Explain(Failed(stderr)));
    }

    [Theory]
    [InlineData("git revert --no-edit abc", "Конфликт при revert")]
    [InlineData("git cherry-pick abc", "Конфликт при cherry-pick")]
    public void Explain_ConflictInRevertOrCherryPick_NamesOperation(string command, string expected)
    {
        var result = new GitResult(command, 1, "CONFLICT (content): Merge conflict in a.txt\n", "error: could not apply abc");

        Assert.StartsWith(expected, GitErrors.Explain(result));
    }

    [Theory]
    [InlineData("error: commit abc is a merge but no -m option was given.", "merge-коммит")]
    [InlineData("The previous cherry-pick is now empty, possibly due to conflict resolution.", "уже есть в ветке")]
    [InlineData("fatal: cherry-pick is already in progress", "не завершена")]
    public void Explain_HistoryEditingErrors_ReturnsFriendlyText(string stderr, string expected)
    {
        var result = new GitResult("git cherry-pick abc", 1, "", stderr);

        Assert.Contains(expected, GitErrors.Explain(result));
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
