using EaseGit.DotNet;

namespace EaseGit.Tests.DotNet;

public sealed class TestSelectionTests
{
    private static readonly string[] All = ["N.A.One", "N.A.Two", "N.B.One", "N.B.Two", "N.B.Three", "N.C.Only"];

    [Fact]
    public void ParseList_StripsParametersAndDeduplicates()
    {
        string[] output =
        [
            "Test run for /x/Tests.dll (.NETCoreApp,Version=v10.0)",
            "The following Tests are available:",
            "    N.A.One",
            "    N.A.Theory(url: \"a(b)\")",
            "    N.A.Theory(url: \"c\")",
            "",
            "Test run for /x/Other.dll",
            "The following Tests are available:",
            "    M.X.Y",
        ];

        Assert.Equal(["N.A.One", "N.A.Theory", "M.X.Y"], TestSelection.ParseList(output));
    }

    [Fact]
    public void Filter_AllSelectedNoneSelected()
    {
        Assert.Null(TestSelection.Filter(All, new HashSet<string>()));
        Assert.Equal("", TestSelection.Filter(All, All.ToHashSet()));
    }

    [Fact]
    public void Filter_FewSelected_IncludesWholeClassesAndSingleTests()
    {
        var excluded = new HashSet<string> { "N.A.Two", "N.B.One", "N.B.Two", "N.B.Three" };

        Assert.Equal("FullyQualifiedName=N.A.One|FullyQualifiedName~N.C.", TestSelection.Filter(All, excluded));
    }

    [Fact]
    public void Filter_FewExcluded_UsesExclusion()
    {
        var excluded = new HashSet<string> { "N.B.Two" };

        Assert.Equal("FullyQualifiedName!=N.B.Two", TestSelection.Filter(All, excluded));
    }

    [Fact]
    public void ClassOf_DropsMethodName()
    {
        Assert.Equal("N.A", TestSelection.ClassOf("N.A.One"));
    }

    [Fact]
    public void FailedTestName_TheoryWithSpacesInParameters()
    {
        Assert.Equal("N.A.Theory(url: \"a b\")", DotnetOutput.FailedTestName("  Failed N.A.Theory(url: \"a b\") [3 ms]"));
    }
}
