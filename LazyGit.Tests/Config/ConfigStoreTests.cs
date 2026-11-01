using LazyGit.Config;

namespace LazyGit.Tests.Config;

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lazygit-config-").FullName;

    private string ConfigPath => Path.Combine(_dir, "config.json");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveAndLoad_Mascot_StoredAsName()
    {
        var store = new ConfigStore(ConfigPath);

        store.Save(new AppConfig { Mascot = Mascot.Gemini });

        Assert.Contains("\"Gemini\"", File.ReadAllText(ConfigPath));
        Assert.Equal(Mascot.Gemini, store.Load().Mascot);
    }

    [Fact]
    public void Load_OldConfigWithoutMascot_DefaultsToNone()
    {
        File.WriteAllText(ConfigPath, """{ "AuthPromptSkipped": true }""");

        var config = new ConfigStore(ConfigPath).Load();

        Assert.True(config.AuthPromptSkipped);
        Assert.Equal(Mascot.None, config.Mascot);
    }

    [Fact]
    public void Load_HandEditedCamelCaseWithCommentsAndTrailingComma_IsAccepted()
    {
        File.WriteAllText(ConfigPath, """
            {
              // тема
              "theme": "ocean",
              "customThemes": { "ocean": { "base": "light", "accent": "#00AAFF", }, },
              "keys": { "files.stage": "s" },
            }
            """);

        var config = new ConfigStore(ConfigPath).Load();

        Assert.Equal("ocean", config.Theme);
        Assert.Equal("#00AAFF", config.CustomThemes!["ocean"]["accent"]);
        Assert.Equal("s", config.Keys!["files.stage"]);
    }

    [Fact]
    public void SaveAndLoad_ThemeAndKeys_RoundTrip()
    {
        var store = new ConfigStore(ConfigPath);

        store.Save(new AppConfig { Theme = "light", Keys = new() { ["commit"] = "ctrl+k" } });

        var loaded = store.Load();
        Assert.Equal("light", loaded.Theme);
        Assert.Equal("ctrl+k", loaded.Keys!["commit"]);
    }

    [Fact]
    public void Load_BrokenJson_ReturnsDefaultsAndKeepsBackup()
    {
        File.WriteAllText(ConfigPath, "{ \"Theme\": ");

        var config = new ConfigStore(ConfigPath).Load();

        Assert.Null(config.Theme);
        Assert.Equal("{ \"Theme\": ", File.ReadAllText(ConfigPath + ".bad"));
    }
}
