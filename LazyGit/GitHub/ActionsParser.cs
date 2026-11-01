using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using LazyGit.Models;

namespace LazyGit.GitHub;

/// <summary>Разбор gh run list / gh run view --json jobs / gh run view --log-failed. Чистые функции.</summary>
public static partial class ActionsParser
{
    public const string RunFields = "databaseId,number,workflowName,displayTitle,headBranch,headSha,event,status,conclusion,createdAt,updatedAt,url";

    public static IReadOnlyList<ActionsRun> ParseRuns(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateArray().Select(run => new ActionsRun(
            Long(run, "databaseId"), (int)Long(run, "number"), Text(run, "workflowName"), Text(run, "displayTitle"), Text(run, "headBranch"),
            Text(run, "headSha"), Text(run, "event"), State(Text(run, "status"), Text(run, "conclusion")), Text(run, "conclusion"),
            Date(run, "createdAt") ?? default, Date(run, "updatedAt") ?? default, Text(run, "url"))).ToList();
    }

    /// <summary>Jobs из gh run view --json jobs.</summary>
    public static IReadOnlyList<ActionsJob> ParseJobs(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("jobs", out var jobs))
            return [];

        return jobs.EnumerateArray().Select(job => new ActionsJob(
            Text(job, "name"), State(Text(job, "status"), Text(job, "conclusion")), Text(job, "conclusion"),
            Date(job, "startedAt"), Date(job, "completedAt"), Text(job, "url"),
            job.TryGetProperty("steps", out var steps)
                ? steps.EnumerateArray().Select(step => new ActionsStep((int)Long(step, "number"), Text(step, "name"),
                    State(Text(step, "status"), Text(step, "conclusion")), Text(step, "conclusion"))).ToList()
                : [])).ToList();
    }

    /// <summary>
    /// «job\tшаг\t[BOM]2026-10-30T14:32:50.54Z текст» → строка без времени. «##[group]» — заголовок без маркера,
    /// «##[endgroup]» пропускается, «##[error]» — ошибка.
    /// </summary>
    public static IReadOnlyList<ActionsLogLine> ParseFailedLog(string output)
    {
        var lines = new List<ActionsLogLine>();
        foreach (var raw in output.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t', 3);
            if (parts.Length < 3)
                continue;

            var text = TimestampRegex().Replace(parts[2].TrimStart('﻿'), "");
            if (text.StartsWith("##[endgroup]", StringComparison.Ordinal))
                continue;

            var error = text.StartsWith("##[error]", StringComparison.Ordinal);
            text = MarkerRegex().Replace(text, "");
            lines.Add(new ActionsLogLine(parts[0], parts[1], text, error));
        }

        return lines;
    }

    /// <summary>Не завершён — идёт; success / skipped / neutral — успех; failure, cancelled, timed_out… — провал.</summary>
    private static CheckState State(string status, string conclusion) =>
        status.Length > 0 && !status.Equals("completed", StringComparison.OrdinalIgnoreCase) ? CheckState.Pending
        : conclusion.ToLowerInvariant() is "success" or "skipped" or "neutral" ? CheckState.Success
        : CheckState.Failure;

    private static string Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";

    private static long Long(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt64() : 0;

    /// <summary>Дата; у незавершённого шага gh отдаёт «0001-01-01T00:00:00Z» — это «нет даты».</summary>
    private static DateTimeOffset? Date(JsonElement element, string name) =>
        DateTimeOffset.TryParse(Text(element, name), CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) && date.Year > 1 ? date : null;

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT[\d:.]+Z ?")]
    private static partial Regex TimestampRegex();

    [GeneratedRegex(@"^##\[(group|error|warning|notice|debug|command)\]")]
    private static partial Regex MarkerRegex();
}
