using LazyGit.GitHub;
using LazyGit.Models;

namespace LazyGit.Tests.GitHub;

public sealed class WorkflowRunParserTests
{
    [Fact]
    public void Parse_RealOutput_MapsStatusAndConclusion()
    {
        const string json = """
            [{"conclusion":"success","databaseId":41347928616,"status":"completed","url":"https://github.com/o/r/actions/runs/41347928616","workflowName":"CI"},
             {"conclusion":"","databaseId":2,"status":"in_progress","url":"u2","workflowName":"Release"},
             {"conclusion":"failure","databaseId":3,"status":"completed","url":"u3","workflowName":"Lint"},
             {"conclusion":"skipped","databaseId":4,"status":"completed","url":"u4","workflowName":"Docs"}]
            """;

        var runs = WorkflowRunParser.Parse(json);

        Assert.Equal([CheckState.Success, CheckState.Pending, CheckState.Failure, CheckState.Success], runs.Select(r => r.State));
        Assert.Equal(41347928616, runs[0].Id);
        Assert.Equal(CheckState.Failure, WorkflowRunParser.Summarize(runs));
        Assert.Equal("Lint", WorkflowRunParser.MostImportant(runs)?.Name);
    }

    [Fact]
    public void Summarize_NoRuns_None()
    {
        Assert.Equal(CheckState.None, WorkflowRunParser.Summarize(WorkflowRunParser.Parse("[]")));
        Assert.Null(WorkflowRunParser.MostImportant([]));
    }
}
