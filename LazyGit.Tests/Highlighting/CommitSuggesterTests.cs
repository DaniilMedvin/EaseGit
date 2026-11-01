using LazyGit.Highlighting;
using LazyGit.Models;

namespace LazyGit.Tests.Highlighting;

public sealed class CommitSuggesterTests
{
    private static CodeChange Add(string name) => new(CodeChangeKind.Added, "метод", name);

    private static CodeChange Mod(string name) => new(CodeChangeKind.Modified, "метод", name);

    [Fact]
    public void Suggest_SingleAddedMethod_FeatWithoutBody()
    {
        var suggestion = CommitSuggester.Suggest([("src/Cache.cs", [Add("Cache.LoadAsync(string, int)")])]);

        Assert.Equal(new CommitSuggestion("feat", "add Cache.LoadAsync", ""), suggestion);
    }

    [Fact]
    public void Suggest_TwoChangesInOneType_NamesBoth()
    {
        var suggestion = CommitSuggester.Suggest([("src/Client.cs", [Mod("Client.Load(string)"), Mod("Client.Save()")])])!;

        Assert.Equal(("refactor", "update Client.Load and Save"), (suggestion.Type, suggestion.Subject));
        Assert.Equal("- update Client.Load\n- update Client.Save", suggestion.Body);
    }

    [Fact]
    public void Suggest_SeveralTypesAndFiles()
    {
        var suggestion = CommitSuggester.Suggest(
        [
            ("src/A.cs", [Add("A.X()"), Mod("A.Y()")]),
            ("src/B.cs", [new CodeChange(CodeChangeKind.Removed, "класс", "B")]),
        ])!;

        Assert.Equal(("feat", "update A, B"), (suggestion.Type, suggestion.Subject));
        Assert.Equal("- add A.X\n- update A.Y\n- remove B", suggestion.Body);
    }

    [Fact]
    public void Suggest_OnlyTests_TestType()
    {
        Assert.Equal("test", CommitSuggester.Suggest([("LazyGit.Tests/ATests.cs", [Add("ATests.Works()")])])!.Type);
        Assert.Null(CommitSuggester.Suggest([]));
    }

    [Fact]
    public void Suggest_ManyChanges_BodyIsLimited()
    {
        var many = Enumerable.Range(0, 20).Select(i => Add($"T{i}.M()")).ToList();

        var body = CommitSuggester.Suggest([("a.cs", many)])!.Body;

        Assert.Equal(CommitSuggester.MaxBodyLines + 1, body.Split('\n').Length);
        Assert.EndsWith("- and 5 more", body);
    }
}
