using System.Globalization;
using System.Text.Json;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Разбор JSON из gh pr list / gh pr view / gh search prs. Чистые функции без IO.</summary>
public static class PullRequestParser
{
    /// <summary>Поля для списка PR одного репозитория.</summary>
    public const string ListFields =
        "number,title,author,url,updatedAt,isDraft,headRefName,baseRefName,reviewDecision,statusCheckRollup,additions,deletions";

    /// <summary>Поля для PR целиком: список и ещё описание и возможность merge.</summary>
    public const string ViewFields = ListFields + ",body,mergeable";

    /// <summary>Поля, которые отдаёт поиск по всем репозиториям.</summary>
    public const string SearchFields = "number,title,author,url,updatedAt,isDraft,repository";

    public static IReadOnlyList<PullRequest> ParseList(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(Read).OfType<PullRequest>().ToList();
    }

    public static PullRequest? ParseOne(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Read(document.RootElement);
    }

    private static PullRequest? Read(JsonElement pr)
    {
        var url = String(pr, "url") ?? "";
        // Репозиторий берётся из адреса: так одинаково для списка, поиска и просмотра.
        if (GitHubRepo.FromWebUrl(url) is not { } repo || !pr.TryGetProperty("number", out var number))
            return null;

        var checks = pr.TryGetProperty("statusCheckRollup", out var rollup) && rollup.ValueKind == JsonValueKind.Array
            ? rollup.EnumerateArray().Select(ReadCheck).ToList()
            : [];
        return new PullRequest(repo, number.GetInt32(), String(pr, "title") ?? "",
            pr.TryGetProperty("author", out var author) ? String(author, "login") ?? "" : "", url,
            DateTimeOffset.TryParse(String(pr, "updatedAt"), CultureInfo.InvariantCulture, out var updated) ? updated : default)
        {
            IsDraft = pr.TryGetProperty("isDraft", out var draft) && draft.ValueKind == JsonValueKind.True,
            HeadRef = String(pr, "headRefName"),
            BaseRef = String(pr, "baseRefName"),
            Review = String(pr, "reviewDecision") switch
            {
                "APPROVED" => ReviewState.Approved,
                "CHANGES_REQUESTED" => ReviewState.ChangesRequested,
                "REVIEW_REQUIRED" => ReviewState.Required,
                _ => ReviewState.None,
            },
            CheckRuns = checks,
            Checks = Summarize(checks),
            Additions = Int(pr, "additions"),
            Deletions = Int(pr, "deletions"),
            Body = String(pr, "body"),
            Mergeable = String(pr, "mergeable"),
        };
    }

    /// <summary>Job Actions (CheckRun: status + conclusion) или внешний статус (StatusContext: state).</summary>
    private static PullRequestCheck ReadCheck(JsonElement check)
    {
        var name = String(check, "name") ?? String(check, "context") ?? "?";
        var workflow = String(check, "workflowName");
        if (!string.IsNullOrEmpty(workflow))
            name = $"{workflow} / {name}";

        CheckState state;
        if (String(check, "state") is { } status)
        {
            state = status switch
            {
                "SUCCESS" => CheckState.Success,
                "PENDING" or "EXPECTED" => CheckState.Pending,
                _ => CheckState.Failure,
            };
        }
        else if (String(check, "status") != "COMPLETED")
        {
            state = CheckState.Pending;
        }
        else
        {
            state = String(check, "conclusion") is "SUCCESS" or "NEUTRAL" or "SKIPPED" ? CheckState.Success : CheckState.Failure;
        }

        return new PullRequestCheck(name, state);
    }

    /// <summary>Худшее из проверок: хоть одна упала — Failure, хоть одна идёт — Pending.</summary>
    public static CheckState Summarize(IReadOnlyList<PullRequestCheck> checks) =>
        checks.Count == 0 ? CheckState.None
        : checks.Any(c => c.State == CheckState.Failure) ? CheckState.Failure
        : checks.Any(c => c.State == CheckState.Pending) ? CheckState.Pending
        : CheckState.Success;

    private static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? Int(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
