using EaseGit.GitHub;
using EaseGit.Models;

namespace EaseGit.Tests.GitHub;

public sealed class ActionsParserTests
{
    [Fact]
    public void ParseRuns_RealOutput()
    {
        const string json = """
            [{"conclusion":"success","createdAt":"2026-11-01T21:15:32Z","databaseId":41351075896,"displayTitle":"feat: file history",
              "event":"push","headBranch":"main","headSha":"173ab1208fb7f46c1db0670eaed4b6ce94741630","number":6,"status":"completed",
              "updatedAt":"2026-11-01T21:16:25Z","url":"https://github.com/o/r/actions/runs/41351075896","workflowName":"CI"},
             {"conclusion":"","createdAt":"2026-11-01T22:00:00Z","databaseId":2,"displayTitle":"wip","event":"pull_request",
              "headBranch":"feature/x","headSha":"abc","number":7,"status":"in_progress","updatedAt":"2026-11-01T22:01:00Z","url":"u","workflowName":"CI"},
             {"conclusion":"cancelled","createdAt":"2026-11-01T22:00:00Z","databaseId":3,"displayTitle":"x","event":"push",
              "headBranch":"main","headSha":"def","number":8,"status":"completed","updatedAt":"2026-11-01T22:01:00Z","url":"u","workflowName":"Release"}]
            """;

        var runs = ActionsParser.ParseRuns(json);

        Assert.Equal(41351075896, runs[0].Id);
        Assert.Equal(("CI", "main", "173ab12", 6), (runs[0].Workflow, runs[0].Branch, runs[0].ShortSha, runs[0].Number));
        Assert.Equal([CheckState.Success, CheckState.Pending, CheckState.Failure], runs.Select(r => r.State));
        Assert.Equal("cancelled", runs[2].Conclusion);
    }

    [Fact]
    public void ParseJobs_StepsAndDuration()
    {
        const string json = """
            {"jobs":[{"completedAt":"2026-11-01T21:16:25Z","conclusion":"failure","name":"test","startedAt":"2026-11-01T21:15:34Z",
              "status":"completed","url":"u","steps":[
                {"conclusion":"success","name":"Set up job","number":1,"status":"completed"},
                {"conclusion":"failure","name":"Test","number":2,"status":"completed"},
                {"conclusion":"","name":"Post","number":3,"status":"queued"}]}]}
            """;

        var job = Assert.Single(ActionsParser.ParseJobs(json));

        Assert.Equal(CheckState.Failure, job.State);
        Assert.Equal(TimeSpan.FromSeconds(51), job.Duration);
        Assert.Equal([CheckState.Success, CheckState.Failure, CheckState.Pending], job.Steps.Select(s => s.State));
    }

    [Fact]
    public void ParseFailedLog_StripsTimestampsAndMarkers()
    {
        const string output =
            "build\tRun tests\t﻿2026-10-30T14:32:50.5431822Z ##[group]Run dotnet test\n" +
            "build\tRun tests\t2026-10-30T14:32:51.0000000Z   Failed Foo.Bar [12 ms]\n" +
            "build\tRun tests\t2026-10-30T14:32:51.1000000Z ##[endgroup]\n" +
            "build\tRun tests\t2026-10-30T14:32:52.0000000Z ##[error]Process completed with exit code 1.\n";

        var lines = ActionsParser.ParseFailedLog(output);

        Assert.Equal(["Run dotnet test", "  Failed Foo.Bar [12 ms]", "Process completed with exit code 1."], lines.Select(l => l.Text));
        Assert.True(lines[2].IsError);
        Assert.Equal(("build", "Run tests"), (lines[0].Job, lines[0].Step));
    }
}
