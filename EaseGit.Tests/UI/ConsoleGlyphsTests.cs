using EaseGit.UI;

namespace EaseGit.Tests.UI;

public class ConsoleGlyphsTests
{
    [Fact]
    public void Choose_FontUnknown_ReturnsPreferred()
    {
        Assert.Equal("⎇", ConsoleGlyphs.Choose("⎇", "±", supports: null));
    }

    [Fact]
    public void Choose_GlyphInFont_ReturnsPreferred()
    {
        Assert.Equal("●", ConsoleGlyphs.Choose("●", "*", _ => true));
    }

    [Fact]
    public void Choose_GlyphMissing_ReturnsFallback()
    {
        // Consolas: ✓ нет, √ есть.
        Assert.Equal("√", ConsoleGlyphs.Choose("✓", "√", text => text != "✓"));
    }
}
