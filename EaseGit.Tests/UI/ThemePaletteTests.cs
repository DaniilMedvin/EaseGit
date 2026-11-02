using EaseGit.Config;
using EaseGit.UI;
using Terminal.Gui.Drawing;

namespace EaseGit.Tests.UI;

public class ThemePaletteTests
{
    [Theory]
    [InlineData("#A6DBFF", true)]
    [InlineData("#a6dbff", true)]
    [InlineData("A6DBFF", false)]
    [InlineData("#FFF", false)]
    [InlineData("#GGGGGG", false)]
    [InlineData("red", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TryParseColor_OnlySixDigitHex(string? text, bool expected)
    {
        Assert.Equal(expected, ThemePalette.TryParseColor(text, out _));
    }

    [Fact]
    public void TryParseColor_ReturnsComponents()
    {
        ThemePalette.TryParseColor("#102030", out var color);

        Assert.Equal((0x10, 0x20, 0x30), (color.R, color.G, color.B));
    }

    [Fact]
    public void With_KnownColor_ReplacesOnlyIt()
    {
        var palette = ThemePalette.Dark.With(new Dictionary<string, string> { ["accent"] = "#FF0000" });

        Assert.Equal(new Color(255, 0, 0), palette.Accent);
        Assert.Equal(ThemePalette.Dark.Muted, palette.Muted);
        Assert.Equal(new Color("#A6DBFF"), ThemePalette.Dark.Accent);
    }

    [Fact]
    public void With_UnknownNameOrBadColor_IsSkippedAndReported()
    {
        var problems = new List<string>();

        var palette = ThemePalette.Dark.With(
            new Dictionary<string, string> { ["nope"] = "#FFFFFF", ["muted"] = "blue" }, problems);

        Assert.Equal(ThemePalette.Dark, palette);
        Assert.Equal(2, problems.Count);
        Assert.Contains("nope", problems[0]);
        Assert.Contains("blue", problems[1]);
    }

    [Fact]
    public void ColorNames_ContainsEveryColorProperty()
    {
        Assert.Contains("Accent", ThemePalette.ColorNames);
        Assert.Contains("SyntaxKeyword", ThemePalette.ColorNames);
        Assert.Contains("Backdrop", ThemePalette.ColorNames);
    }

    [Fact]
    public void Light_DiffersFromDarkInEveryColor()
    {
        foreach (var name in ThemePalette.ColorNames)
        {
            var property = typeof(ThemePalette).GetProperty(name)!;
            Assert.NotEqual(property.GetValue(ThemePalette.Dark), property.GetValue(ThemePalette.Light));
        }
    }
}

public class ThemeCatalogTests
{
    private static AppConfig WithCustom(string name, Dictionary<string, string> colors, string? current = null) => new()
    {
        Theme = current ?? name,
        CustomThemes = new() { [name] = colors },
    };

    [Fact]
    public void Resolve_NoTheme_IsDark()
    {
        var (name, palette, problem) = ThemeCatalog.Resolve(new AppConfig());

        Assert.Equal("dark", name);
        Assert.Equal(ThemePalette.Dark, palette);
        Assert.Null(problem);
    }

    [Theory]
    [InlineData("light")]
    [InlineData("LIGHT")]
    public void Resolve_Light_IgnoresCase(string theme)
    {
        Assert.Equal(ThemePalette.Light, ThemeCatalog.Resolve(new AppConfig { Theme = theme }).Palette);
    }

    [Fact]
    public void Resolve_UnknownTheme_FallsBackToDarkWithProblem()
    {
        var (name, palette, problem) = ThemeCatalog.Resolve(new AppConfig { Theme = "ocean" });

        Assert.Equal("dark", name);
        Assert.Equal(ThemePalette.Dark, palette);
        Assert.Contains("ocean", problem);
    }

    [Fact]
    public void Resolve_Custom_UsesBaseAndOverrides()
    {
        var config = WithCustom("ocean", new() { ["base"] = "light", ["accent"] = "#00AAFF" });

        var (name, palette, problem) = ThemeCatalog.Resolve(config);

        Assert.Equal("ocean", name);
        Assert.Equal(new Color(0, 0xAA, 0xFF), palette.Accent);
        Assert.Equal(ThemePalette.Light.Muted, palette.Muted);
        Assert.Null(problem);
    }

    [Fact]
    public void Resolve_CustomWithTypo_StillAppliesRestAndReports()
    {
        var config = WithCustom("ocean", new() { ["accnt"] = "#00AAFF", ["muted"] = "#111111" });

        var (_, palette, problem) = ThemeCatalog.Resolve(config);

        Assert.Equal(new Color("#111111"), palette.Muted);
        Assert.Contains("accnt", problem);
    }

    [Fact]
    public void Names_BuiltInFirstThenCustomSorted_NoDuplicatesOfBuiltIn()
    {
        var config = new AppConfig
        {
            CustomThemes = new()
            {
                ["zebra"] = new(),
                ["Dark"] = new(),
                ["alpha"] = new(),
            },
        };

        Assert.Equal(["dark", "light", "alpha", "zebra"], ThemeCatalog.Names(config));
    }

    [Fact]
    public void Next_CyclesThroughAllAndWraps()
    {
        var config = WithCustom("ocean", new(), current: "dark");

        var first = ThemeCatalog.Next(config);
        var second = ThemeCatalog.Next(config with { Theme = first });
        var third = ThemeCatalog.Next(config with { Theme = second });

        Assert.Equal(["light", "ocean", "dark"], [first, second, third]);
    }

    [Fact]
    public void Next_UnknownCurrent_GoesToLightAfterDarkFallback()
    {
        Assert.Equal("light", ThemeCatalog.Next(new AppConfig { Theme = "missing" }));
    }
}
