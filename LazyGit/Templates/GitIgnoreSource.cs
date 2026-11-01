namespace LazyGit.Templates;

/// <summary>Откуда взят шаблон .gitignore.</summary>
public enum GitIgnoreOrigin
{
    /// <summary>Скачан с GitHub: всегда актуальная версия.</summary>
    GitHub,

    /// <summary>Встроенная копия: GitHub недоступен.</summary>
    Embedded,
}

public sealed record GitIgnoreResult(string Content, GitIgnoreOrigin Origin);

/// <summary>
/// Загрузка актуального шаблона Visual Studio из репозитория github/gitignore.
/// Без сети используется встроенная копия, чтобы создание файла не зависело от интернета.
/// </summary>
public static class GitIgnoreSource
{
    public const string Url = "https://raw.githubusercontent.com/github/gitignore/main/VisualStudio.gitignore";

    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    /// <summary>Свои правила, которых нет в шаблоне GitHub: секреты окружения и папка IDE.</summary>
    private const string Extras = """
        # Секреты локальной среды
        .env
        .env.local
        .env.prod

        # IDE
        .idea/
        """;

    public static async Task<GitIgnoreResult> LoadAsync(HttpClient http, CancellationToken ct = default)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            var text = await http.GetStringAsync(Url, timeout.Token);
            if (LooksLikeGitIgnore(text))
                return new GitIgnoreResult(WithExtras(text), GitIgnoreOrigin.GitHub);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            // Нет сети, таймаут или ошибка сервера: ниже берём встроенную копию.
        }

        return new GitIgnoreResult(GitIgnoreTemplate.VisualStudio, GitIgnoreOrigin.Embedded);
    }

    /// <summary>Защита от подмены ответом-страницей (прокси, captive portal): в настоящем шаблоне есть типичные правила.</summary>
    public static bool LooksLikeGitIgnore(string text) =>
        text.Length > 500 && text.Contains(".vs/") && !text.Contains("<html", StringComparison.OrdinalIgnoreCase);

    /// <summary>Добавляет собственные правила в конец, если шаблон их ещё не содержит.</summary>
    public static string WithExtras(string upstream)
    {
        var lines = upstream.ReplaceLineEndings("\n").Split('\n').Select(l => l.Trim()).ToHashSet();
        var missing = Extras.ReplaceLineEndings("\n").Split('\n')
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !lines.Contains(l))
            .ToList();
        if (missing.Count == 0)
            return upstream.ReplaceLineEndings().TrimEnd() + Environment.NewLine;

        return (upstream.TrimEnd() + "\n\n" + Extras + "\n").ReplaceLineEndings();
    }
}
