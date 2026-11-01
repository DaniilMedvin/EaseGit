using LazyGit.Config;

namespace LazyGit.UI;

/// <summary>Имя темы → палитра: встроенные dark и light и пользовательские темы из config.json.</summary>
public static class ThemeCatalog
{
    public const string Dark = "dark";
    public const string Light = "light";

    /// <summary>Все доступные темы: сначала встроенные, затем пользовательские по алфавиту.</summary>
    public static IReadOnlyList<string> Names(AppConfig config) =>
        [Dark, Light, .. (config.CustomThemes?.Keys.AsEnumerable() ?? []).Where(n => !IsBuiltIn(n)).Order(StringComparer.OrdinalIgnoreCase)];

    /// <summary>
    /// Палитра выбранной темы. Неизвестная тема — тёмная. У пользовательской темы ключ «base» («dark» или «light»)
    /// выбирает основу, остальные ключи — цвета. Проблемы (опечатки) возвращаются текстом для строки статуса.
    /// </summary>
    public static (string Name, ThemePalette Palette, string? Problem) Resolve(AppConfig config)
    {
        var name = config.Theme ?? Dark;
        if (name.Equals(Dark, StringComparison.OrdinalIgnoreCase))
            return (Dark, ThemePalette.Dark, null);
        if (name.Equals(Light, StringComparison.OrdinalIgnoreCase))
            return (Light, ThemePalette.Light, null);

        var custom = config.CustomThemes?.FirstOrDefault(t => t.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (custom is not { Value: { } colors })
            return (Dark, ThemePalette.Dark, $"тема «{name}» не найдена, включена dark");

        var baseName = colors.FirstOrDefault(c => c.Key.Equals("base", StringComparison.OrdinalIgnoreCase)).Value;
        var basePalette = string.Equals(baseName, Light, StringComparison.OrdinalIgnoreCase) ? ThemePalette.Light : ThemePalette.Dark;
        var problems = new List<string>();
        var palette = basePalette.With(
            colors.Where(c => !c.Key.Equals("base", StringComparison.OrdinalIgnoreCase)).ToDictionary(c => c.Key, c => c.Value),
            problems);
        return (custom.Value.Key, palette, problems.Count == 0 ? null : $"тема «{name}»: {problems[0]}");
    }

    /// <summary>Следующая тема по кругу после текущей (для переключения одной клавишей).</summary>
    public static string Next(AppConfig config)
    {
        var names = Names(config);
        var current = Resolve(config).Name;
        var index = names.ToList().FindIndex(n => n.Equals(current, StringComparison.OrdinalIgnoreCase));
        return names[(index + 1) % names.Count];
    }

    private static bool IsBuiltIn(string name) =>
        name.Equals(Dark, StringComparison.OrdinalIgnoreCase) || name.Equals(Light, StringComparison.OrdinalIgnoreCase);
}
