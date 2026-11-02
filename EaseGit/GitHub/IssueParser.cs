using System.Globalization;
using System.Text.Json;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Разбор gh issue list / gh issue view --json. Чистые функции без IO.</summary>
public static class IssueParser
{
    public const string ListFields = "number,title,author,labels,assignees,updatedAt,url,comments,state";

    public const string ViewFields = ListFields + ",body,createdAt";

    public static IReadOnlyList<Issue> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(Read).OfType<Issue>().ToList();
    }

    public static Issue? ParseOne(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    private static Issue? Read(JsonElement issue)
    {
        var url = Text(issue, "url");
        if (GitHubRepo.FromWebUrl(url) is not { } repo || !issue.TryGetProperty("number", out var number))
            return null;

        var comments = issue.TryGetProperty("comments", out var list) && list.ValueKind == JsonValueKind.Array ? list : default;
        return new Issue(repo, number.GetInt32(), Text(issue, "title"), Login(issue, "author"), url, Date(issue, "updatedAt") ?? default)
        {
            Labels = Names(issue, "labels", "name"),
            Assignees = Names(issue, "assignees", "login"),
            CommentCount = comments.ValueKind == JsonValueKind.Array ? comments.GetArrayLength() : 0,
            State = Text(issue, "state"),
            Body = issue.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String ? body.GetString() : null,
            CreatedAt = Date(issue, "createdAt"),
            Comments = comments.ValueKind == JsonValueKind.Array
                ? comments.EnumerateArray().Select(c => new IssueComment(Login(c, "author"), Date(c, "createdAt") ?? default, Text(c, "body"))).ToList()
                : [],
        };
    }

    private static IReadOnlyList<string> Names(JsonElement element, string array, string field) =>
        element.TryGetProperty(array, out var items) && items.ValueKind == JsonValueKind.Array
            ? items.EnumerateArray().Select(i => Text(i, field)).Where(n => n.Length > 0).ToList()
            : [];

    private static string Login(JsonElement element, string name) =>
        element.TryGetProperty(name, out var author) && author.ValueKind == JsonValueKind.Object ? Text(author, "login") : "";

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static DateTimeOffset? Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
