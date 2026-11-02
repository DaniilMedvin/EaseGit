using System.Text.RegularExpressions;

namespace EaseGit.DotNet;

/// <summary>Разбор вывода dotnet build / test (английский, DOTNET_CLI_UI_LANGUAGE=en). Чистые функции.</summary>
public static partial class DotnetOutput
{
    public static DotnetLineKind Classify(string line)
    {
        var text = line.TrimStart();
        if (FailedTestRegex().IsMatch(line))
            return DotnetLineKind.FailedTest;
        if (text.StartsWith("Build succeeded", StringComparison.Ordinal) || text.StartsWith("Build FAILED", StringComparison.Ordinal)
            || TestSummaryRegex().IsMatch(text))
            return DotnetLineKind.Summary;
        if (line.Contains(": error ", StringComparison.Ordinal) || text.StartsWith("error ", StringComparison.Ordinal))
            return DotnetLineKind.Error;
        if (line.Contains(": warning ", StringComparison.Ordinal))
            return DotnetLineKind.Warning;
        return DotnetLineKind.Normal;
    }

    /// <summary>Имя упавшего теста из «  Failed Namespace.Class.Method [12 ms]» или null.</summary>
    public static string? FailedTestName(string line) =>
        FailedTestRegex().Match(line) is { Success: true } match ? match.Groups[1].Value : null;

    /// <summary>Сумма итогов тестов по всем сборкам: «Passed!  - Failed: 1, Passed: 10, Skipped: 0, Total: 11».</summary>
    public static (int Failed, int Passed, int Skipped, int Total)? TestTotals(IEnumerable<string> lines)
    {
        (int, int, int, int)? sum = null;
        foreach (var line in lines)
        {
            var match = TestSummaryRegex().Match(line.TrimStart());
            if (!match.Success)
                continue;

            var (failed, passed, skipped, total) = sum ?? (0, 0, 0, 0);
            sum = (failed + int.Parse(match.Groups["failed"].Value), passed + int.Parse(match.Groups["passed"].Value),
                skipped + int.Parse(match.Groups["skipped"].Value), total + int.Parse(match.Groups["total"].Value));
        }

        return sum;
    }

    /// <summary>Ошибки сборки без повторов: dotnet печатает каждую дважды — по ходу и в итоге.</summary>
    public static int CountErrors(IEnumerable<string> lines) =>
        lines.Where(l => Classify(l) == DotnetLineKind.Error).Select(l => l.Trim()).Distinct().Count();

    public static int CountWarnings(IEnumerable<string> lines) =>
        lines.Where(l => Classify(l) == DotnetLineKind.Warning).Select(l => l.Trim()).Distinct().Count();

    // Имя до « [12 ms]»: у [Theory] в нём параметры с пробелами — «Method(url: "x")».
    [GeneratedRegex(@"^\s+Failed (.+?) \[\d")]
    private static partial Regex FailedTestRegex();

    [GeneratedRegex(@"^(Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+(?<total>\d+)")]
    private static partial Regex TestSummaryRegex();
}
