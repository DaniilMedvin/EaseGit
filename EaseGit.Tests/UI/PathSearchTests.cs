using EaseGit.UI;

namespace EaseGit.Tests.UI;

public sealed class PathSearchTests
{
    private static readonly string[] Paths =
        ["docs/PLAN.md", "EaseGit/UI/MainWindow.cs", "EaseGit/UI/Panels/DiffPanel.cs", "EaseGit/Git/Parsers/DiffParser.cs", "src/Мой файл.cs"];

    [Fact]
    public void Filter_NameStartFirstThenNameThenPath()
    {
        Assert.Equal(["EaseGit/UI/Panels/DiffPanel.cs", "EaseGit/Git/Parsers/DiffParser.cs"], PathSearch.Filter(Paths, "diff"));
        Assert.Equal(["EaseGit/UI/MainWindow.cs", "EaseGit/UI/Panels/DiffPanel.cs"], PathSearch.Filter(Paths, "ui/"));
        Assert.Equal(["EaseGit/UI/Panels/DiffPanel.cs"], PathSearch.Filter(Paths, "panel"));
    }

    [Fact]
    public void Filter_CyrillicAndEmpty()
    {
        Assert.Equal(["src/Мой файл.cs"], PathSearch.Filter(Paths, "мой"));
        Assert.Equal(Paths.Length, PathSearch.Filter(Paths, " ").Count);
        Assert.Empty(PathSearch.Filter(Paths, "nothing"));
    }
}
