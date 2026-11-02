using System.Text.Json;
using EaseGit.Models;

namespace EaseGit.GitHub;

/// <summary>Разбор gh run list --json. Чистые функции без IO.</summary>
public static class WorkflowRunParser
{
    public const string Fields = "databaseId,workflowName,status,conclusion,url";

    public static IReadOnlyList<WorkflowRun> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(run => new WorkflowRun(
            run.TryGetProperty("databaseId", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt64() : 0,
            Text(run, "workflowName"),
            // queued, in_progress, waiting… — ещё идёт; success, skipped, neutral — успех; остальное — провал.
            Text(run, "status") != "completed" ? CheckState.Pending
            : Text(run, "conclusion") is "success" or "skipped" or "neutral" ? CheckState.Success
            : CheckState.Failure,
            Text(run, "url"))).ToList();
    }

    /// <summary>Итог по всем запускам: худший, как у проверок PR.</summary>
    public static CheckState Summarize(IReadOnlyList<WorkflowRun> runs) =>
        PullRequestParser.Summarize(runs.Select(r => new PullRequestCheck(r.Name, r.State)).ToList());

    /// <summary>Запуск, который стоит открыть: упавший, затем идущий, затем любой; null — запусков нет.</summary>
    public static WorkflowRun? MostImportant(IReadOnlyList<WorkflowRun> runs) =>
        runs.FirstOrDefault(r => r.State == CheckState.Failure)
        ?? runs.FirstOrDefault(r => r.State == CheckState.Pending)
        ?? runs.FirstOrDefault();

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
}
