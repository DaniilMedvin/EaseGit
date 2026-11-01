namespace LazyGit.Templates;

/// <summary>Быстрое создание стандартных файлов репозитория: README.md и .gitignore.</summary>
public static class RepoScaffold
{
    public const string ReadmeFileName = "README.md";

    public static bool ReadmeExists(string repoRoot) => File.Exists(Path.Combine(repoRoot, ReadmeFileName));

    public static bool GitIgnoreExists(string repoRoot) => File.Exists(Path.Combine(repoRoot, GitIgnoreTemplate.FileName));

    /// <summary>Содержимое нового README: заголовок с именем папки репозитория и место для описания.</summary>
    public static string ReadmeContent(string repoRoot)
    {
        var name = new DirectoryInfo(repoRoot).Name;
        return $"# {name}\n\nКраткое описание проекта.\n".ReplaceLineEndings();
    }

    /// <summary>Создаёт README.md. Существующий файл не перезаписывается: возвращает false.</summary>
    public static bool CreateReadme(string repoRoot) =>
        CreateIfMissing(Path.Combine(repoRoot, ReadmeFileName), ReadmeContent(repoRoot));

    /// <summary>Создаёт .gitignore со стандартными правилами Visual Studio. Существующий файл не перезаписывается.</summary>
    public static bool CreateGitIgnore(string repoRoot) =>
        CreateIfMissing(Path.Combine(repoRoot, GitIgnoreTemplate.FileName), GitIgnoreTemplate.VisualStudio);

    private static bool CreateIfMissing(string path, string content)
    {
        try
        {
            // CreateNew атомарно отказывает, если файл уже есть, — без гонки между проверкой и записью.
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false));
            writer.Write(content);
            return true;
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }
    }
}
