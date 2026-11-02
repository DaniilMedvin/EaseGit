namespace EaseGit.DotNet;

/// <summary>
/// Список тестов из <c>dotnet test --list-tests</c> и фильтр <c>--filter</c> по выбору. Тест — полное имя метода
/// (у [Theory] все наборы данных — один тест). Чистые функции.
/// </summary>
public static class TestSelection
{
    /// <summary>Полные имена тестов в порядке вывода, без повторов и без параметров «(url: "…")».</summary>
    public static IReadOnlyList<string> ParseList(IEnumerable<string> lines)
    {
        var tests = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var listing = false;
        foreach (var line in lines)
        {
            if (line.StartsWith("The following Tests are available", StringComparison.Ordinal))
            {
                listing = true;
                continue;
            }

            // Список — строки с отступом после заголовка; у каждой сборки тестов свой заголовок.
            if (!listing || !line.StartsWith("    ", StringComparison.Ordinal))
            {
                if (line.Trim().Length > 0)
                    listing = false;
                continue;
            }

            var name = line.Trim();
            var paren = name.IndexOf('(');
            if (paren > 0)
                name = name[..paren];
            if (seen.Add(name))
                tests.Add(name);
        }

        return tests;
    }

    /// <summary>«EaseGit.Tests.UI.CommitGraphTests.Build_X» → «EaseGit.Tests.UI.CommitGraphTests».</summary>
    public static string ClassOf(string test) => test[..Math.Max(0, test.LastIndexOf('.'))];

    /// <summary>
    /// Фильтр для dotnet test: null — выбраны все (фильтр не нужен), "" — не выбрано ничего. Целиком выбранный класс —
    /// одно условие «FullyQualifiedName~Класс.», остальные — по тесту. Если исключённых меньше, чем выбранных, фильтр строится
    /// через исключение: так он короче (на Windows длина командной строки ограничена).
    /// </summary>
    public static string? Filter(IReadOnlyList<string> all, IReadOnlySet<string> excluded)
    {
        var selected = all.Where(t => !excluded.Contains(t)).ToList();
        if (selected.Count == all.Count)
            return null;
        if (selected.Count == 0)
            return "";

        var include = Conditions(all, selected, "~", "=");
        var exclude = Conditions(all, all.Where(excluded.Contains).ToList(), "!~", "!=");
        return include.Count <= exclude.Count ? string.Join('|', include) : string.Join('&', exclude);
    }

    private static List<string> Conditions(IReadOnlyList<string> all, IReadOnlyList<string> tests, string classOperator, string testOperator)
    {
        var chosen = tests.ToHashSet(StringComparer.Ordinal);
        var conditions = new List<string>();
        foreach (var group in tests.GroupBy(ClassOf))
        {
            var whole = all.Where(t => ClassOf(t) == group.Key).All(chosen.Contains);
            if (whole)
                conditions.Add($"FullyQualifiedName{classOperator}{Escape(group.Key)}.");
            else
                conditions.AddRange(group.Select(t => $"FullyQualifiedName{testOperator}{Escape(t)}"));
        }

        return conditions;
    }

    /// <summary>Символы, особые в синтаксисе фильтра VSTest, экранируются.</summary>
    private static string Escape(string name) =>
        name.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("&", "\\&").Replace("|", "\\|")
            .Replace("=", "\\=").Replace("!", "\\!").Replace("~", "\\~");
}
