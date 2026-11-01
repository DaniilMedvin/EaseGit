using LazyGit.Git.Parsers;
using LazyGit.Highlighting;
using LazyGit.Models;
using LazyGit.UI.Panels;

namespace LazyGit.Tests.Highlighting;

public sealed class MarkupAndJsonHighlighterTests
{
    private static List<(string Text, TokenClass Class)> Tokens(string text, IReadOnlyList<HighlightSpan> spans) =>
        spans.Select(s => (text.Substring(s.Start, s.Length), s.Class)).ToList();

    [Fact]
    public void Markup_ProjectFile()
    {
        const string text = """<PackageReference Include="Terminal.Gui" Version="2.5.0" /> <!-- note -->""";

        var tokens = Tokens(text, MarkupHighlighter.Highlight(text, razor: false));

        Assert.Contains(("PackageReference", TokenClass.Keyword), tokens);
        Assert.Contains(("Include", TokenClass.Variable), tokens);
        Assert.Contains(("\"Terminal.Gui\"", TokenClass.String), tokens);
        Assert.Contains(("<!-- note -->", TokenClass.Comment), tokens);
        Assert.Contains(("<", TokenClass.Punctuation), tokens);
        Assert.Contains((">", TokenClass.Punctuation), tokens);
    }

    [Fact]
    public void Markup_RazorDirectivesAndExpressions()
    {
        const string text = "@page \"/x\"\n@if (ok) { <p>@Model.Name</p> } @* comment *@";

        var tokens = Tokens(text, MarkupHighlighter.Highlight(text, razor: true));

        Assert.Contains(("@page", TokenClass.ControlKeyword), tokens);
        Assert.Contains(("@if", TokenClass.ControlKeyword), tokens);
        Assert.Contains(("@Model.Name", TokenClass.Variable), tokens);
        Assert.Contains(("p", TokenClass.Keyword), tokens);
        Assert.Contains(("@* comment *@", TokenClass.Comment), tokens);
    }

    [Fact]
    public void Json_KeysValuesAndComments()
    {
        const string text = "{\n  // comment\n  \"Logging\": { \"Level\": \"Debug\", \"Port\": 8080, \"On\": true, \"X\": null }\n}";

        var tokens = Tokens(text, JsonHighlighter.Highlight(text));

        Assert.Contains(("\"Logging\"", TokenClass.Variable), tokens);
        Assert.Contains(("\"Debug\"", TokenClass.String), tokens);
        Assert.Contains(("8080", TokenClass.Number), tokens);
        Assert.Contains(("true", TokenClass.Keyword), tokens);
        Assert.Contains(("null", TokenClass.Keyword), tokens);
        Assert.Contains(("// comment", TokenClass.Comment), tokens);
    }

    [Theory]
    [InlineData("App.csproj", true, false)]
    [InlineData("Views/Index.razor", true, false)]
    [InlineData("appsettings.json", true, false)]
    [InlineData("Program.cs", true, true)]
    [InlineData("readme.md", false, false)]
    public void Supported_HighlightVsCSharpAnalysis(string path, bool highlighted, bool csharp)
    {
        Assert.Equal(highlighted, DiffHighlighter.IsSupported(path));
        Assert.Equal(csharp, DiffHighlighter.IsCSharp(path));
    }

    [Fact]
    public void Prepare_ProjectFile_AddsNuGetNotesOnTop()
    {
        var lines = DiffParser.Parse(string.Join('\n',
            "diff --git a/App.csproj b/App.csproj",
            "--- a/App.csproj",
            "+++ b/App.csproj",
            "@@ -1,4 +1,4 @@",
            "-    <PackageReference Include=\"Terminal.Gui\" Version=\"2.4.0\" />",
            "+    <PackageReference Include=\"Terminal.Gui\" Version=\"2.5.0\" />",
            "-    <PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"4.9.0\" />",
            "+    <PackageReference Include=\"Microsoft.CodeAnalysis.CSharp\" Version=\"5.9.0\" />",
            "+    <PackageReference Include=\"Serilog\" Version=\"4.0.0\" />",
            "-    <PackageReference Include=\"Newtonsoft.Json\" Version=\"13.0.3\" />") + "\n");

        var prepared = DiffPanel.Prepare(lines, "App.csproj");
        var notes = prepared.Lines.TakeWhile(l => l.Kind == DiffLineKind.Note).ToList();

        Assert.Equal(4, notes.Count);
        Assert.Equal("NuGet: Terminal.Gui 2.4.0 → 2.5.0", notes[0].Text);
        Assert.True(notes[1].IsWarning);
        Assert.Equal("NuGet: + Serilog 4.0.0", notes[2].Text);
        Assert.Equal("NuGet: − Newtonsoft.Json 13.0.3", notes[3].Text);
        Assert.Equal(prepared.Lines.Count, prepared.Highlights.Length);
        Assert.DoesNotContain(DiffPanel.Prepare(lines, "notes.txt").Lines, l => l.Kind == DiffLineKind.Note);
    }
}
