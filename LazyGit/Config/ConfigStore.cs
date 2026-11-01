using System.Text.Json;

namespace LazyGit.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly string _path;

    public ConfigStore(string? path = null)
    {
        _path = path ?? DefaultPath();
    }

    public AppConfig Load()
    {
        try
        {
            if (!File.Exists(_path))
                return new AppConfig();

            return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_path), JsonOptions) ?? new AppConfig();
        }
        catch (JsonException)
        {
            // Повреждённый конфиг заменяется значениями по умолчанию, а правки пользователя сохраняются рядом:
            // следующее сохранение перезапишет config.json.
            BackUpBrokenFile();
            return new AppConfig();
        }
    }

    private void BackUpBrokenFile()
    {
        try
        {
            File.Copy(_path, _path + ".bad", overwrite: true);
        }
        catch (IOException)
        {
            // Нет места или прав: работаем с настройками по умолчанию без копии.
        }
    }

    public void Save(AppConfig config)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        File.WriteAllText(_path, JsonSerializer.Serialize(config, JsonOptions));
    }

    private static string DefaultPath()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

        return Path.Combine(configHome, "lazygit-cs", "config.json");
    }
}
