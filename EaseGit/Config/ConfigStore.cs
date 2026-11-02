using System.Text.Json;

namespace EaseGit.Config;

public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    private readonly string _path;

    public ConfigStore(string? path = null)
    {
        if (path is null)
        {
            path = Path.Combine(ConfigHome(), "easegit", "config.json");
            CopyLegacy(Path.Combine(ConfigHome(), "lazygit-cs", "config.json"), path);
        }

        _path = path;
    }

    /// <summary>
    /// Раньше программа называлась LGit и хранила настройки в lazygit-cs: при первом запуске под новым именем
    /// они копируются в новый файл. Старый файл остаётся — на случай, если вернуться к старой версии.
    /// </summary>
    public static void CopyLegacy(string legacyPath, string path)
    {
        try
        {
            if (File.Exists(path) || !File.Exists(legacyPath))
                return;

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.Copy(legacyPath, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Не вышло — начнём с настроек по умолчанию.
        }
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

    private static string ConfigHome()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        return string.IsNullOrEmpty(configHome)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config")
            : configHome;
    }
}
