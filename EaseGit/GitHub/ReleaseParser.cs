using System.Text.Json;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Разбор gh release list --json. Чистые функции без IO.</summary>
public static class ReleaseParser
{
    public const string ListFields = "tagName,name,isDraft,isPrerelease,isLatest";

    public static IReadOnlyList<GitHubRelease> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray()
            .Select(r => new GitHubRelease(Text(r, "tagName"), Text(r, "name"), Flag(r, "isDraft"), Flag(r, "isPrerelease"), Flag(r, "isLatest")))
            .Where(r => r.TagName.Length > 0)
            .ToList();
    }

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
