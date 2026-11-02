using EaseGit.DotNet;

namespace EaseGit.Tests.DotNet;

public sealed class DotnetTests
{
    [Fact]
    public void Output_ClassifiesAndCountsWithoutDuplicates()
    {
        string[] lines =
        [
            "/src/A.cs(10,5): error CS0103: The name 'x' does not exist [/src/App.csproj]",
            "/src/B.cs(3,1): warning CS0108: hides member [/src/App.csproj]",
            "Build FAILED.",
            "/src/A.cs(10,5): error CS0103: The name 'x' does not exist [/src/App.csproj]",
            "    1 Warning(s)",
            "  Failed EaseGit.Tests.Git.Foo.Bar_Works [21 ms]",
            "Failed!  - Failed:     1, Passed:   525, Skipped:     0, Total:   526, Duration: 1 s - EaseGit.Tests.dll (net10.0)",
            "Passed!  - Failed:     0, Passed:    10, Skipped:     2, Total:    12, Duration: 1 s - Other.Tests.dll (net10.0)",
        ];

        Assert.Equal(DotnetLineKind.Error, DotnetOutput.Classify(lines[0]));
        Assert.Equal(DotnetLineKind.Warning, DotnetOutput.Classify(lines[1]));
        Assert.Equal(DotnetLineKind.Summary, DotnetOutput.Classify(lines[2]));
        Assert.Equal(DotnetLineKind.Normal, DotnetOutput.Classify(lines[4]));
        Assert.Equal(DotnetLineKind.FailedTest, DotnetOutput.Classify(lines[5]));
        Assert.Equal("EaseGit.Tests.Git.Foo.Bar_Works", DotnetOutput.FailedTestName(lines[5]));
        Assert.Equal(1, DotnetOutput.CountErrors(lines));
        Assert.Equal(1, DotnetOutput.CountWarnings(lines));
        Assert.Equal((1, 535, 2, 538), DotnetOutput.TestTotals(lines));
        Assert.Null(DotnetOutput.TestTotals(["Build succeeded."]));
    }

    [Fact]
    public void Target_PrefersRootSolutionThenSingleProject()
    {
        var root = Directory.CreateTempSubdirectory("easegit-dotnet-").FullName;
        try
        {
            void Touch(string path)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(root, path))!);
                File.WriteAllText(Path.Combine(root, path), "");
            }

            Assert.Null(DotnetTarget.Find(root));
            Touch("src/App/App.csproj");
            Touch("src/App/bin/Debug/Copy.csproj");
            Assert.Equal("src/App/App.csproj", DotnetTarget.Find(root));
            Touch("tests/App.Tests/App.Tests.csproj");
            Assert.Null(DotnetTarget.Find(root));
            Touch("src/Deep.sln");
            Assert.Equal("src/Deep.sln", DotnetTarget.Find(root));
            Touch("App.slnx");
            Assert.Equal("App.slnx", DotnetTarget.Find(root));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Target_ThisRepository_IsItsSolution()
    {
        var repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../.."));
        Assert.Equal("EaseGit.sln", DotnetTarget.Find(repo));
    }
}
