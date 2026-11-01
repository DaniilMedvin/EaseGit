namespace LazyGit.DotNet;

/// <summary>Что собирать в репозитории: решение или проект.</summary>
public static class DotnetTarget
{
    private const int MaxDepth = 3;
    private static readonly string[] Skipped = ["bin", "obj", "node_modules", ".git", ".vs", ".idea"];

    /// <summary>
    /// Решение (.sln / .slnx) в корне; если его нет — ближайшее по глубине; иначе единственный .csproj / .fsproj;
    /// несколько проектов без решения или ничего — null. Путь относительный от корня.
    /// </summary>
    public static string? Find(string repoRoot)
    {
        var files = Enumerate(repoRoot, repoRoot, 0).ToList();
        var solutions = files.Where(f => f.EndsWith(".sln", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .OrderBy(Depth).ThenBy(f => f, StringComparer.Ordinal).ToList();
        if (solutions.Count > 0)
            return solutions[0];

        var projects = files.Where(f => f.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)).ToList();
        return projects.Count == 1 ? projects[0] : null;
    }

    private static int Depth(string path) => path.Count(c => c == '/');

    private static IEnumerable<string> Enumerate(string root, string directory, int depth)
    {
        IEnumerable<string> files, directories;
        try
        {
            files = Directory.EnumerateFiles(directory).ToList();
            directories = depth < MaxDepth ? Directory.EnumerateDirectories(directory).ToList() : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var file in files)
            yield return Path.GetRelativePath(root, file).Replace('\\', '/');
        foreach (var child in directories.Where(d => !Skipped.Contains(Path.GetFileName(d), StringComparer.OrdinalIgnoreCase)))
        {
            foreach (var file in Enumerate(root, child, depth + 1))
                yield return file;
        }
    }
}
