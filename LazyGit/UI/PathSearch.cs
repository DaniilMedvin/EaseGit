namespace LazyGit.UI;

/// <summary>Поиск файла по части пути: сначала совпадение с началом имени, потом в имени, потом где угодно в пути.</summary>
public static class PathSearch
{
    public const int MaxResults = 500;

    public static IReadOnlyList<string> Filter(IReadOnlyList<string> paths, string query)
    {
        var text = query.Trim();
        if (text.Length == 0)
            return paths.Take(MaxResults).ToList();

        return paths
            .Select(path => (path, rank: Rank(path, text)))
            .Where(p => p.rank >= 0)
            .OrderBy(p => p.rank)
            .ThenBy(p => p.path.Length)
            .Take(MaxResults)
            .Select(p => p.path)
            .ToList();
    }

    /// <summary>0 — имя начинается с запроса, 1 — запрос в имени, 2 — в пути, -1 — не подходит. Регистр не важен.</summary>
    private static int Rank(string path, string query)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.Contains(query, StringComparison.OrdinalIgnoreCase))
            return 1;
        return path.Contains(query, StringComparison.OrdinalIgnoreCase) ? 2 : -1;
    }
}
