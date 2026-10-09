using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

internal static class SpecRunEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Spec runs");

        group.MapPost("repos/{repoId:int}/spec-runs", QueueSpecAsync)
            .AddEndpointFilter<OperationalOnlyFilter>()
            .WithName("QueueSpecRun")
            .WithSummary("Enqueues a parent spec issue. 202 with the run id; 503 with the prerequisite status in diagnostic-only mode.")
            .Produces<QueueSpecResponse>(StatusCodes.Status202Accepted)
            .Produces<QueueSpecResponse>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("repos/{repoId:int}/spec-runs", ListQueueAsync)
            .WithName("ListSpecRuns")
            .Produces<IReadOnlyList<SpecRunView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("spec-runs/{id}", async (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ApiIds.SpecRun(id) is { } runId && await queries.GetSpecRunAsync(runId, cancellationToken) is { } run
                ? (IResult)TypedResults.Ok(run)
                : SpecRunNotFound(id))
            .WithName("GetSpecRun")
            .Produces<SpecRunView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("spec-runs/{id}/tickets", (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ForSpecRun(id, queries, cancellationToken, queries.ListTicketRunsAsync))
            .WithName("ListSpecRunTickets")
            .Produces<IReadOnlyList<TicketRunView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("spec-runs/{id}/events", (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ForSpecRun(id, queries, cancellationToken, queries.ListEventsAsync))
            .WithName("ListSpecRunEvents")
            .WithSummary("Persisted audit events of the run. See the events/stream endpoint for live updates.")
            .Produces<IReadOnlyList<RunEventView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("spec-runs/{id}/stack", (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ForSpecRun(id, queries, cancellationToken, queries.ListStackAsync))
            .WithName("ListSpecRunStack")
            .Produces<IReadOnlyList<StackLayerView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("spec-runs/{id}/merge-status", async (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ApiIds.SpecRun(id) is { } runId && await queries.GetSpecRunAsync(runId, cancellationToken) is { } run
                ? (IResult)TypedResults.Ok(MergeStatusProjection.From(run, await queries.ListStackAsync(runId, cancellationToken)))
                : SpecRunNotFound(id))
            .WithName("GetSpecRunMergeStatus")
            .WithSummary("Whether the run's PR stack is awaiting the human merge, merged into trunk, or closed unmerged, as last tracked.")
            .Produces<MergeStatusView>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<IResult> QueueSpecAsync(
        int repoId,
        [FromBody] QueueSpecRequest request,
        [FromServices] ISpecEnqueuer enqueuer,
        CancellationToken cancellationToken)
    {
        if (request.SpecIssueNumber < 1)
        {
            return ApiProblems.Validation([new(nameof(request.SpecIssueNumber), "Must be a positive issue number.")]);
        }

        EnqueueResult result = await enqueuer.EnqueueAsync(repoId, request.SpecIssueNumber, cancellationToken);
        return result.Outcome switch
        {
            EnqueueOutcome.Queued => TypedResults.Accepted($"/api/spec-runs/{result.SpecRunId!.Value.Value}", new QueueSpecResponse(result.SpecRunId.Value.Value, nameof(EnqueueOutcome.Queued))),
            EnqueueOutcome.AlreadyQueued => TypedResults.Ok(new QueueSpecResponse(result.SpecRunId!.Value.Value, nameof(EnqueueOutcome.AlreadyQueued))),
            EnqueueOutcome.RepositoryNotFound => ApiProblems.NotFound($"Repository {repoId} does not exist."),
            _ => ApiProblems.Conflict("The queue was changed concurrently; retry."),
        };
    }

    private static async Task<IResult> ListQueueAsync(
        int repoId,
        [FromServices] IRepositoryQueries repositories,
        [FromServices] IRunQueries runs,
        CancellationToken cancellationToken) =>
        await repositories.GetAsync(repoId, cancellationToken) is null
            ? ApiProblems.NotFound($"Repository {repoId} does not exist.")
            : TypedResults.Ok(await runs.ListSpecRunsAsync(repoId, cancellationToken));

    /// <summary>Lists something that belongs to a spec run, answering 404 when the run itself does not exist.</summary>
    private static async Task<IResult> ForSpecRun<T>(
        string id,
        IRunQueries queries,
        CancellationToken cancellationToken,
        Func<RunId, CancellationToken, Task<IReadOnlyList<T>>> list)
    {
        if (ApiIds.SpecRun(id) is not { } runId || await queries.GetSpecRunAsync(runId, cancellationToken) is null)
        {
            return SpecRunNotFound(id);
        }

        return TypedResults.Ok(await list(runId, cancellationToken));
    }

    private static IResult SpecRunNotFound(string id) => ApiProblems.NotFound($"Spec run '{id}' does not exist.");
}
