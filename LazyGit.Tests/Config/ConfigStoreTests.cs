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
}
