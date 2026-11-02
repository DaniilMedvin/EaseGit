using EaseGit.Highlighting;

namespace EaseGit.Tests.Highlighting;

public class CSharpHighlighterTests
{
    /// <summary>Класс токена для каждого вхождения <paramref name="token"/> в тексте.</summary>
    private static List<TokenClass> ClassesOf(string code, string token)
    {
        var spans = CSharpHighlighter.Highlight(code);
        var result = new List<TokenClass>();
        for (var index = code.IndexOf(token, StringComparison.Ordinal); index >= 0; index = code.IndexOf(token, index + 1, StringComparison.Ordinal))
        {
            var span = spans.FirstOrDefault(s => s.Start <= index && index < s.End);
            result.Add(span.Length == 0 ? TokenClass.Plain : span.Class);
        }

        return result;
    }

    private static TokenClass ClassOf(string code, string token) => ClassesOf(code, token)[0];

    [Theory]
    [InlineData("public sealed class Foo { }", "public", TokenClass.Keyword)]
    [InlineData("public sealed class Foo { }", "class", TokenClass.Keyword)]
    [InlineData("public sealed class Foo { }", "Foo", TokenClass.Class)]
    [InlineData("struct Point { }", "Point", TokenClass.Struct)]
    [InlineData("record struct Pair(int A);", "Pair", TokenClass.Struct)]
    [InlineData("record Person(string Name);", "Person", TokenClass.Class)]
    [InlineData("interface IRunner { }", "IRunner", TokenClass.Interface)]
    [InlineData("enum Color { Red }", "Color", TokenClass.Enum)]
    public void Highlight_TypeDeclarations(string code, string token, TokenClass expected)
    {
        Assert.Equal(expected, ClassOf(code, token));
    }

    [Theory]
    [InlineData("if (x) return;", "if")]
    [InlineData("if (x) return;", "return")]
    [InlineData("foreach (var a in b) { break; }", "foreach")]
    [InlineData("try { } catch { } finally { }", "catch")]
    [InlineData("throw new Exception();", "throw")]
    public void Highlight_ControlKeywords(string code, string token)
    {
        Assert.Equal(TokenClass.ControlKeyword, ClassOf(code, token));
    }

    [Fact]
    public void Highlight_LiteralsAndComments()
    {
        const string code = "var s = \"hi\"; var n = 42; // note\n/* block */ var c = 'x';";

        Assert.Equal(TokenClass.Keyword, ClassOf(code, "var"));
        Assert.Equal(TokenClass.String, ClassOf(code, "\"hi\""));
        Assert.Equal(TokenClass.Number, ClassOf(code, "42"));
        Assert.Equal(TokenClass.Comment, ClassOf(code, "// note"));
        Assert.Equal(TokenClass.Comment, ClassOf(code, "/* block */"));
        Assert.Equal(TokenClass.String, ClassOf(code, "'x'"));
    }

    [Fact]
    public void Highlight_MethodsAndStaticTypes()
    {
        const string code = "class A { void Run() { Console.WriteLine(1); Helper(); } }";

        Assert.Equal(TokenClass.Method, ClassOf(code, "Run"));
        Assert.Equal(TokenClass.Class, ClassOf(code, "Console"));
        Assert.Equal(TokenClass.Method, ClassOf(code, "WriteLine"));
        Assert.Equal(TokenClass.Method, ClassOf(code, "Helper"));
    }

    [Fact]
    public void Highlight_TypesInTypePositions()
    {
        const string code = "class A : IFoo, Base { List<string> Items = new List<string>(); Dictionary<int, Node> Map; }";

        Assert.Equal(TokenClass.Interface, ClassOf(code, "IFoo"));
        Assert.Equal(TokenClass.Class, ClassOf(code, "Base"));
        Assert.All(ClassesOf(code, "List"), c => Assert.Equal(TokenClass.Class, c));
        Assert.Equal(TokenClass.Class, ClassOf(code, "Node"));
        Assert.Equal(TokenClass.Keyword, ClassOf(code, "string"));
    }

    [Fact]
    public void Highlight_ParametersAndLocals_DeclarationAndUsage()
    {
        const string code = "int Add(int left) { var total = left + 1; return total; }";

        Assert.All(ClassesOf(code, "left"), c => Assert.Equal(TokenClass.Variable, c));
        Assert.All(ClassesOf(code, "total"), c => Assert.Equal(TokenClass.Variable, c));
    }

    [Fact]
    public void Highlight_MemberWithSameNameAsLocal_IsNotVariable()
    {
        const string code = "void M() { var count = 1; Console.WriteLine(list.count); }";

        Assert.Equal([TokenClass.Variable, TokenClass.Plain], ClassesOf(code, "count"));
    }

    [Fact]
    public void Highlight_TypeParameters()
    {
        const string code = "T Get<T>(TKey key) where T : class => default;";

        Assert.All(ClassesOf(code, "T"), c => Assert.Equal(TokenClass.TypeParameter, c));
    }

    [Fact]
    public void Highlight_UsingAndNamespace_ArePlain()
    {
        const string code = "using System.Text;\nnamespace EaseGit.Models;";

        Assert.Equal(TokenClass.Plain, ClassOf(code, "System"));
        Assert.Equal(TokenClass.Plain, ClassOf(code, "Text"));
        Assert.Equal(TokenClass.Plain, ClassOf(code, "Models"));
    }

    [Fact]
    public void Highlight_Attribute_IsClass()
    {
        Assert.Equal(TokenClass.Class, ClassOf("[Fact] void Test() { }", "Fact"));
    }

    [Fact]
    public void Highlight_InterpolatedString()
    {
        const string code = "var name = \"x\"; var s = $\"Hello {name}!\";";

        Assert.Equal(TokenClass.String, ClassOf(code, "$\"Hello "));
        Assert.Equal(TokenClass.Variable, ClassesOf(code, "name")[1]);
        Assert.Equal(TokenClass.String, ClassOf(code, "!\""));
    }

    [Fact]
    public void Highlight_DocCommentAndPreprocessor()
    {
        const string code = "/// <summary>Docs</summary>\n#if DEBUG\nclass A { }\n#endif";

        Assert.Equal(TokenClass.Comment, ClassOf(code, "<summary>"));
        Assert.Equal(TokenClass.Comment, ClassOf(code, "Docs"));
        Assert.Equal(TokenClass.Preprocessor, ClassOf(code, "#if DEBUG"));
    }

    [Fact]
    public void Highlight_IncompleteFragment_DoesNotThrow()
    {
        const string code = "        if (!File.Exists(_path))\n            return new AppConfig();\n    }\n}";

        Assert.Equal(TokenClass.ControlKeyword, ClassOf(code, "if"));
        Assert.Equal(TokenClass.Class, ClassOf(code, "File"));
        Assert.Equal(TokenClass.Method, ClassOf(code, "Exists"));
        Assert.Equal(TokenClass.Class, ClassOf(code, "AppConfig"));
    }

    [Fact]
    public void Highlight_SpansAreSortedAndDoNotOverlap()
    {
        var spans = CSharpHighlighter.Highlight("/// <summary>x</summary>\npublic class A { int B() => 1; }");

        for (var i = 1; i < spans.Count; i++)
            Assert.True(spans[i].Start >= spans[i - 1].End);
    }
}
