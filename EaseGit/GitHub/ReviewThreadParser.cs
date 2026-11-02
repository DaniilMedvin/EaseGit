using System.Globalization;
using System.Text.Json;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>
/// Обсуждения строк PR через GraphQL (gh api graphql): REST не отдаёт, решено ли обсуждение. Чистые функции без IO.
/// </summary>
public static class ReviewThreadParser
{
    /// <summary>До 100 обсуждений по 100 комментариев: больше на одном PR почти не бывает.</summary>
    public const string Query = """
        query($owner: String!, $name: String!, $number: Int!) {
          repository(owner: $owner, name: $name) {
            pullRequest(number: $number) {
              headRefOid
              reviewThreads(first: 100) {
                nodes {
                  id isResolved isOutdated path line originalLine diffSide
                  comments(first: 100) { nodes { author { login } body createdAt } }
                }
              }
            }
          }
        }
        """;

    public static PullRequestThreads Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("data", out var data)
            || !data.TryGetProperty("repository", out var repository) || repository.ValueKind != JsonValueKind.Object
            || !repository.TryGetProperty("pullRequest", out var pr) || pr.ValueKind != JsonValueKind.Object)
            return new PullRequestThreads("", []);

        var threads = pr.TryGetProperty("reviewThreads", out var list) && list.TryGetProperty("nodes", out var nodes)
                      && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().Select(Read).OfType<ReviewThread>().ToList()
            : [];
        return new PullRequestThreads(Text(pr, "headRefOid"), threads);
    }

    private static ReviewThread? Read(JsonElement thread)
    {
        var id = Text(thread, "id");
        var path = Text(thread, "path");
        if (id.Length == 0 || path.Length == 0)
            return null;

        var comments = thread.TryGetProperty("comments", out var list) && list.TryGetProperty("nodes", out var nodes)
                       && nodes.ValueKind == JsonValueKind.Array
            ? nodes.EnumerateArray().Select(c => new ReviewComment(Login(c), Date(c, "createdAt"), Text(c, "body"))).ToList()
            : [];
        return new ReviewThread(id, path, Number(thread, "line"), Number(thread, "originalLine"),
            Text(thread, "diffSide") == "LEFT" ? ReviewSide.Left : ReviewSide.Right,
            Flag(thread, "isResolved"), Flag(thread, "isOutdated"), comments);
    }

    private static string Login(JsonElement comment) =>
        comment.TryGetProperty("author", out var author) && author.ValueKind == JsonValueKind.Object ? Text(author, "login") : "ghost";

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static int? Number(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : default;
}
