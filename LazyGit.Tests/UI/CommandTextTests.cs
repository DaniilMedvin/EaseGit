using LazyGit.UI;

namespace LazyGit.Tests.UI;

public class CommandTextTests
{
    [Fact]
    public void Format_PathWithSpace_IsQuoted()
    {
        Assert.Equal("git restore -- 'my file.txt' b.txt", CommandText.Format(["restore", "--", "my file.txt", "b.txt"]));
    }

    [Fact]
    public void Format_ManyPaths_ShortensList()
    {
        Assert.Equal("git clean --force -- a b c … ещё 2", CommandText.Format(["clean", "--force", "--", "a", "b", "c", "d", "e"]));
    }

    [Fact]
    public void Format_QuoteInPath_IsEscaped()
    {
        Assert.Equal("git add -- 'it'\\''s.txt'", CommandText.Format(["add", "--", "it's.txt"]));
    }
}
