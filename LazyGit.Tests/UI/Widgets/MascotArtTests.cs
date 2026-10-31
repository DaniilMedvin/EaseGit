using System.Reflection;
using LazyGit.UI.Widgets;

namespace LazyGit.Tests.UI.Widgets;

public class MascotArtTests
{
    public static TheoryData<string> Grids => new() { "Claude", "ChatGpt", "Gemini" };

    [Theory]
    [MemberData(nameof(Grids))]
    public void Grid_MatchesDeclaredSize(string name)
    {
        var grid = (string[])typeof(MascotArt).GetField(name, BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

        Assert.Equal(MascotArt.Rows * 2, grid.Length);
        Assert.All(grid, row => Assert.Equal(MascotArt.Width, row.Length));
    }
}
