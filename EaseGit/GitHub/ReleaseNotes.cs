using System.Text.RegularExpressions;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>
/// Черновик заметок релиза из коммитов с прошлого тега: по типам Conventional Commits — ломающие изменения, новое,
/// исправления, остальное; merge-коммиты пропускаются. Заголовки на английском: заметки читают на GitHub, а не в EaseGit.
/// </summary>
public static partial class ReleaseNotes
{
    /// <param name="commits">Коммиты релиза, новые первыми (как отдаёт git log).</param>
    /// <param name="previousTag">Прошлый тег: с ним в конце ссылка на полное сравнение.</param>
    public static string Build(IReadOnlyList<Commit> commits, GitHubRepo repo, string tag, string? previousTag)
    {
        var breaking = new List<string>();
        var features = new List<string>();
        var fixes = new List<string>();
        var other = new List<string>();
        foreach (var commit in commits.Where(c => c.Parents.Count <= 1).Reverse())
        {
            var match = ConventionalRegex().Match(commit.Subject.Trim());
            var text = match.Success ? match.Groups["text"].Value : commit.Subject.Trim();
            var scope = match.Groups["scope"].Success ? $"**{match.Groups["scope"].Value}:** " : "";
            var item = $"- {scope}{text} ({commit.ShortHash})";
            var type = match.Success ? match.Groups["type"].Value.ToLowerInvariant() : "";
            (match.Groups["bang"].Success ? breaking : type == "feat" ? features : type == "fix" ? fixes : other).Add(item);
        }

        var sections = new List<string>();
        void Section(string title, List<string> items)
        {
            if (items.Count > 0)
                sections.Add($"### {title}\n\n{string.Join('\n', items)}");
        }

        Section("Breaking changes", breaking);
        Section("Features", features);
        Section("Fixes", fixes);
        Section("Other changes", other);
        if (previousTag is not null)
            sections.Add($"**Full changelog**: https://github.com/{repo.NameWithOwner}/compare/{previousTag}...{tag}");
        return string.Join("\n\n", sections);
    }

    /// <summary>Пре-релиз по SemVer: после номера версии идёт «-…» (v1.2.0-beta.1, 2.0.0-rc1).</summary>
    public static bool IsPrerelease(string tag) => PrereleaseRegex().IsMatch(tag);

    [GeneratedRegex(@"^(?<type>[A-Za-z]+)(\((?<scope>[^)]+)\))?(?<bang>!)?:\s*(?<text>.+)$")]
    private static partial Regex ConventionalRegex();

    [GeneratedRegex(@"^v?\d+(\.\d+)*-\S+$")]
    private static partial Regex PrereleaseRegex();
}
