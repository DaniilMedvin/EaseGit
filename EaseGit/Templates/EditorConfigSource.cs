using EaseGit.DotNet;

namespace EaseGit.Templates;

/// <summary>Откуда взят .editorconfig.</summary>
public enum EditorConfigOrigin
{
    /// <summary>Создан установленным .NET SDK: стандарт его версии.</summary>
    Sdk,

    /// <summary>Встроенная копия: dotnet не найден или шаблон не сработал.</summary>
    Embedded,
}

public sealed record EditorConfigResult(string Content, EditorConfigOrigin Origin);

/// <summary>
/// Стандартный .editorconfig из шаблона .NET SDK (<c>dotnet new editorconfig</c>) — как .gitignore из github/gitignore.
/// Создаётся во временной папке: в репозиторий файл кладёт <see cref="RepoScaffold"/>, не перезаписывая существующий.
/// </summary>
public static class EditorConfigSource
{
    public const string Command = "dotnet new editorconfig";

    // Первый запуск dotnet new может долго строить кэш шаблонов.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    public static async Task<EditorConfigResult> LoadAsync(DotnetRunner dotnet, CancellationToken ct = default)
    {
        var dir = Directory.CreateTempSubdirectory("easegit-editorconfig-").FullName;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var exit = await dotnet.RunAsync(dir, ["new", "editorconfig", "--output", dir], _ => { }, timeout.Token);
            var path = Path.Combine(dir, EditorConfigTemplate.FileName);
            if (exit == 0 && File.Exists(path) && await File.ReadAllTextAsync(path, ct) is var text && LooksLikeEditorConfig(text))
                return new EditorConfigResult(text.ReplaceLineEndings().TrimEnd() + Environment.NewLine, EditorConfigOrigin.Sdk);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Таймаут: ниже берём встроенную копию.
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
                // Временная папка — не страшно, если осталась.
            }
        }

        return new EditorConfigResult(EditorConfigTemplate.Default, EditorConfigOrigin.Embedded);
    }

    /// <summary>Защита от пустого или чужого вывода: в настоящем шаблоне есть корень и секция C#.</summary>
    public static bool LooksLikeEditorConfig(string text) =>
        text.Contains("root = true", StringComparison.Ordinal) && text.Contains("[*.cs]", StringComparison.Ordinal);
}
