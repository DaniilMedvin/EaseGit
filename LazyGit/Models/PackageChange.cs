namespace LazyGit.Models;

/// <summary>Изменение NuGet-пакета в файле проекта: версия была → стала; null — пакета не было (или его убрали).</summary>
public sealed record PackageChange(string Name, string? From, string? To)
{
    /// <summary>Сменилась первая цифра версии: по SemVer возможны ломающие изменения.</summary>
    public bool IsMajor =>
        From is not null && To is not null && Major(From) is { } from && Major(To) is { } to && from != to;

    private static int? Major(string version) =>
        int.TryParse(version.Split('.', '-')[0].TrimStart('[', '('), out var major) ? major : null;
}
