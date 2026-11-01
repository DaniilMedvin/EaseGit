namespace LazyGit.UI;

/// <summary>Git-команда в виде строки для показа пользователю (подтверждения, Command Log).</summary>
public static class CommandText
{
    private const int MaxPaths = 3;

    /// <summary>
    /// «git restore -- 'my file.txt' b.txt». Аргументы с пробелами и кавычками берутся в одинарные кавычки,
    /// чтобы команду можно было скопировать в терминал. Больше трёх путей после «--» сокращаются: «… ещё 5».
    /// </summary>
    public static string Format(IReadOnlyList<string> args)
    {
        var separator = args.ToList().IndexOf("--");
        var shown = args.Select(Quote).ToList();
        if (separator >= 0 && args.Count - separator - 1 > MaxPaths)
        {
            var hidden = args.Count - separator - 1 - MaxPaths;
            shown = [.. shown.Take(separator + 1 + MaxPaths), T($"… ещё {hidden}", $"… {hidden} more")];
        }

        return "git " + string.Join(' ', shown);
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c is '\'' or '"' or '$' or '*' or '?' or '`')
            ? arg
            : "'" + arg.Replace("'", "'\\''") + "'";
}
