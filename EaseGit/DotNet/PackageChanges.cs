using System.Text.RegularExpressions;
using EaseGit.Models;

namespace EaseGit.DotNet;

/// <summary>
/// Изменения NuGet-пакетов по diff файла проекта (.csproj, Directory.Packages.props…): строки
/// <c>&lt;PackageReference Include="X" Version="1.2" /&gt;</c> и <c>&lt;PackageVersion …&gt;</c> среди удалённых и добавленных,
/// сопоставленные по имени пакета. Чистые функции.
/// </summary>
public static partial class PackageChanges
{
    private static readonly HashSet<string> ProjectExtensions = new(StringComparer.OrdinalIgnoreCase) { ".csproj", ".fsproj", ".vbproj", ".props", ".targets" };

    public static bool IsProjectFile(string path) => ProjectExtensions.Contains(Path.GetExtension(path));

    public static IReadOnlyList<PackageChange> Detect(IReadOnlyList<DiffLine> lines)
    {
        var removed = Packages(lines, DiffLineKind.Removed);
        var added = Packages(lines, DiffLineKind.Added);
        var changes = new List<PackageChange>();
        foreach (var (name, version) in added)
        {
            if (!removed.TryGetValue(name, out var old))
                changes.Add(new PackageChange(name, null, version));
            else if (old != version)
                changes.Add(new PackageChange(name, old, version));
        }

        changes.AddRange(removed.Where(p => !added.ContainsKey(p.Key)).Select(p => new PackageChange(p.Key, p.Value, null)));
        return changes;
    }

    private static Dictionary<string, string> Packages(IReadOnlyList<DiffLine> lines, DiffLineKind kind)
    {
        var packages = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in lines.Where(l => l.Kind == kind))
        {
            var match = PackageRegex().Match(line.Text);
            if (!match.Success)
                continue;

            var attributes = AttributeRegex().Matches(match.Value).ToDictionary(m => m.Groups[1].Value, m => m.Groups[2].Value, StringComparer.OrdinalIgnoreCase);
            var name = attributes.GetValueOrDefault("Include") ?? attributes.GetValueOrDefault("Update");
            if (name is not null && attributes.TryGetValue("Version", out var version))
                packages[name] = version;
        }

        return packages;
    }

    [GeneratedRegex(@"<(PackageReference|PackageVersion|GlobalPackageReference)\b[^>]*>")]
    private static partial Regex PackageRegex();

    [GeneratedRegex(@"\b(\w+)\s*=\s*""([^""]*)""")]
    private static partial Regex AttributeRegex();
}
