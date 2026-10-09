using System.Net;
using System.Text.Json;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Tests.Api;

public sealed class MergeStatusEndpointTests
{
    private static readonly StackLayerView[] Stack =
    [
        new(1, "t-1", "stack/run-1/t-1", "main", "aaa", 21, 3, false, "aaa"),
        new(2, "t-2", "stack/run-1/t-2", "stack/run-1/t-1", "bbb", 22, 3, false, "bbb"),
    ];

    [Theory]
    [InlineData(SpecRunStatus.AwaitingMerge, null, "Awaiting")]
    [InlineData(SpecRunStatus.Completed, null, "Merged")]
    [InlineData(SpecRunStatus.NeedsAttention, SpecRunStatus.AwaitingMerge, "Closed")]
    public async Task merge_status_reports_awaiting_merged_and_closed_stacks(SpecRunStatus status, SpecRunStatus? failedIn, string expected)
    {
        await using var factory = new ApiFactory();
        factory.Runs.SpecRuns.Add(ApiData.SpecRun("run-1", status: status) with { NeedsAttentionFrom = failedIn, FailureReason = failedIn is null ? null : "closed unmerged" });
        factory.Runs.Stack.AddRange(Stack);
        using HttpClient client = factory.CreateClient();

        JsonElement body = await client.GetJsonAsync("/api/spec-runs/run-1/merge-status");

        Assert.Equal(("run-1", expected), (body.GetProperty("specRunId").GetString(), body.GetProperty("state").GetString()));
        Assert.Equal([21, 22], body.GetProperty("pullRequests").EnumerateArray().Select(number => number.GetInt32()));
        Assert.Equal("bbb", body.GetProperty("topCommitSha").GetString());
    }

    [Fact]
    public async Task merge_status_of_an_unknown_run_is_not_found()
    {
        await using var factory = new ApiFactory();
        using HttpClient client = factory.CreateClient();

        using HttpResponseMessage response = await client.GetResponseAsync("/api/spec-runs/nope/merge-status");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
