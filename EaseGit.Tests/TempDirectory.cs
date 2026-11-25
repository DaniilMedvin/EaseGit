namespace EaseGit.Tests;

/// <summary>Удаление временных папок тестов.</summary>
public static class TempDirectory
{
    /// <summary>
    /// Удаляет папку со всем содержимым. На Windows git делает файлы в .git/objects «только для чтения»,
    /// и Directory.Delete на них падает с UnauthorizedAccessException — атрибут снимается заранее.
    /// Файл может ненадолго держать только что завершившийся git, поэтому есть несколько попыток.
    /// </summary>
    public static void Delete(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (!Directory.Exists(path))
                return;

            try
            {
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                    File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < 5)
            {
                Thread.Sleep(100 * attempt);
            }
        }
    }
}
