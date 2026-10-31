namespace LazyGit.Commands;

/// <summary>Нечёткий поиск для палитры: буквы запроса должны встречаться в тексте по порядку.</summary>
public static class CommandSearch
{
    /// <summary>
    /// Очки совпадения или null, если не совпало. Больше очков — выше в списке:
    /// за совпадение подряд, в начале слова и в начале текста. Регистр не важен.
    /// </summary>
    public static int? Score(string query, string text)
    {
        query = query.Trim();
        if (query.Length == 0)
            return 0;

        var score = 0;
        var position = 0;
        var previous = -2;
        foreach (var c in query)
        {
            if (char.IsWhiteSpace(c))
                continue;

            var found = text.IndexOf(c.ToString(), position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
                return null;

            score += 1;
            if (found == previous + 1)
                score += 5;
            if (found == 0 || !char.IsLetterOrDigit(text[found - 1]))
                score += 3;

            previous = found;
            position = found + 1;
        }

        return score;
    }

    /// <summary>Команды, подходящие под запрос, лучшие первыми; при равных очках — в исходном порядке.</summary>
    public static IReadOnlyList<AppCommand> Filter(IEnumerable<AppCommand> commands, string query) =>
        commands
            .Select((command, index) => (command, index, score: Best(query, command)))
            .Where(x => x.score is not null)
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)
            .Select(x => x.command)
            .ToList();

    private static int? Best(string query, AppCommand command) =>
        new[] { Score(query, command.Title), Score(query, command.Id), Score(query, command.Key.Label), Score(query, command.Keywords) }
            .Where(s => s is not null)
            .DefaultIfEmpty(null)
            .Max();
}
