using LazyGit.UI;

namespace LazyGit.Tests.UI;

public sealed class PathSearchTests
{
    private static readonly string[] Paths =
        ["docs/PLAN.md", "LazyGit/UI/MainWindow.cs", "LazyGit/UI/Panels/DiffPanel.cs", "LazyGit/Git/Parsers/DiffParser.cs", "src/Мой файл.cs"];

    [Fact]
    public void Filter_NameStartFirstThenNameThenPath()
    {
        Assert.Equal(["LazyGit/UI/Panels/DiffPanel.cs", "LazyGit/Git/Parsers/DiffParser.cs"], PathSearch.Filter(Paths, "diff"));
        Assert.Equal(["LazyGit/UI/MainWindow.cs", "LazyGit/UI/Panels/DiffPanel.cs"], PathSearch.Filter(Paths, "ui/"));
        Assert.Equal(["LazyGit/UI/Panels/DiffPanel.cs"], PathSearch.Filter(Paths, "panel"));
    }

    [Fact]
    public void Filter_CyrillicAndEmpty()
    {
        Assert.Equal(["src/Мой файл.cs"], PathSearch.Filter(Paths, "мой"));
        Assert.Equal(Paths.Length, PathSearch.Filter(Paths, " ").Count);
        Assert.Empty(PathSearch.Filter(Paths, "nothing"));
    }
}
