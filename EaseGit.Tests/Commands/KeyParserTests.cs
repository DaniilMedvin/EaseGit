using EaseGit.Commands;
using Terminal.Gui.Input;

namespace EaseGit.Tests.Commands;

public class KeyParserTests
{
    private static CommandKey Parsed(string text)
    {
        var (key, error) = KeyParser.Parse(text);
        Assert.True(key is not null, error);
        return key;
    }

    [Fact]
    public void Parse_LowercaseLetter_MatchesPlainKey()
    {
        var key = Parsed("s");

        Assert.True(key.Matches(Key.S));
        Assert.False(key.Matches(Key.S.WithShift));
        Assert.Equal("s", key.Label);
    }

    [Fact]
    public void Parse_UppercaseLetter_MeansShift()
    {
        var key = Parsed("D");

        Assert.True(key.Matches(Key.D.WithShift));
        Assert.False(key.Matches(Key.D));
        Assert.Equal("D", key.Label);
    }

    [Theory]
    [InlineData("ctrl+x", "ctrl+x")]
    [InlineData("Ctrl+X", "ctrl+x")]
    public void Parse_Ctrl_MatchesCtrlKey(string text, string label)
    {
        var key = Parsed(text);

        Assert.True(key.Matches(Key.X.WithCtrl));
        Assert.Equal(label, key.Label);
    }

    [Fact]
    public void Parse_Named_MatchesSpecialKeys()
    {
        Assert.True(Parsed("space").Matches(Key.Space));
        Assert.True(Parsed("enter").Matches(Key.Enter));
        Assert.True(Parsed("esc").Matches(Key.Esc));
        Assert.True(Parsed("tab").Matches(Key.Tab));
        Assert.True(Parsed("up").Matches(Key.CursorUp));
        Assert.True(Parsed("F5").Matches(Key.F5));
        Assert.True(Parsed("f12").Matches(Key.F12));
        Assert.True(Parsed("1").Matches(Key.D1));
    }

    [Fact]
    public void Parse_Symbol_MatchesByCharacter()
    {
        Assert.True(Parsed("?").Matches(new Key('?')));
        Assert.True(Parsed("~").Matches(new Key('~')));
        Assert.Equal("?", Parsed("?").Label);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ctrl+")]
    [InlineData("hyper")]
    [InlineData("ctrl+?")]
    [InlineData("f13")]
    public void Parse_Invalid_ReturnsError(string text)
    {
        var (key, error) = KeyParser.Parse(text);

        Assert.Null(key);
        Assert.False(string.IsNullOrEmpty(error));
    }
}
