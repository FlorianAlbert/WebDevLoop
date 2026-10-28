using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.SpecQueue;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

internal static class SpecRunEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Spec runs");

        group.MapPost("repos/{repoId:int}/spec-runs/batch", QueueSelectionAsync)
            .AddEndpointFilter<OperationalOnlyFilter>()
            .WithName("QueueSpecSelection")
            .WithSummary("Queues selected parent specs sequentially in the supplied order.")
            .WithDescription("Returns one ordered result per issue: Queued, AlreadyQueued, RepositoryNotFound, Ineligible, GitHubUnavailable, ConcurrencyConflict, Unavailable or Failed. Each result includes an actionable message and a new or existing run id when available. The shared Core enqueue path rechecks eligibility and duplicates in an independent unit of work; a failed item does not hide or prevent later results. Does not reorder existing runs. Diagnostic-only mode returns 503 before submission; readiness changes during the batch produce per-item Unavailable results.")
            .Produces<IReadOnlyList<QueueSpecItemResult>>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("repos/{repoId:int}/spec-candidates", ListCandidatesAsync)
            .WithName("ListSpecCandidates")
            .WithSummary("Finds open, opted-in parent specs with independently opted-in open sub-issues.")
            .WithDescription("Reads current GitHub state. Excludes pull requests, sub-issues and specs with unfinished runs. Search matches issue number or title, case-insensitively. onlyUnblocked checks only open native parent blockers, including other repositories; ticket blockers never exclude a candidate. Start at page 1 and follow nextPage with the same filters until null. Empty source pages are skipped. Refresh by starting at page 1; submission must still use QueueSpecRun to recheck eligibility.")
            .Produces<SpecCandidatePage>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("repos/{repoId:int}/spec-runs", QueueSpecAsync)
            .AddEndpointFilter<OperationalOnlyFilter>()
            .WithName("QueueSpecRun")
            .WithSummary("Enqueues an open parent spec with WebDevLoop Opt-in and at least one independently opted-in open sub-issue.")
            .WithDescription("Checks current GitHub state and the fixed 'webdevloop' label at submission, including typed input. Triage labels do not authorize work; eligible tickets may all be blocked. Returns 202 with the new run id, 200 AlreadyQueued with the existing unfinished run, 409 for ineligibility or a concurrent change, and 503 when GitHub eligibility cannot be verified or prerequisites fail. Failure problem details explain the reason; no run is created.")
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

    private static async Task<IResult> QueueSelectionAsync(
        int repoId, [FromBody] QueueSpecsRequest request, [FromServices] ISpecSelectionEnqueuer enqueuer,
        CancellationToken cancellationToken)
    {
        if (request.SpecIssueNumbers is not { Count: > 0 } numbers || numbers.Any(number => number < 1))
        {
            return ApiProblems.Validation([new(nameof(request.SpecIssueNumbers), "Select at least one positive issue number.")]);
        }

        IReadOnlyList<SpecSelectionResult> results = await enqueuer.EnqueueAsync(repoId, numbers, cancellationToken);
        return TypedResults.Ok(results.Select(result =>
            new QueueSpecItemResult(result.SpecIssueNumber, result.Outcome, result.SpecRunId?.Value, result.Message)).ToArray());
    }

    private static async Task<IResult> ListCandidatesAsync(
        int repoId, [FromServices] ISpecCandidateQueries queries, CancellationToken cancellationToken,
        string? search = null, bool onlyUnblocked = false, int page = 1)
    {
        if (page < 1)
        {
            return ApiProblems.Validation([new(nameof(page), "Must be a positive page number.")]);
        }

        try
        {
            CommandResult<SpecCandidatePage> result = await queries.ListAsync(repoId, search, onlyUnblocked, page, cancellationToken);
            return result.IsSuccess ? TypedResults.Ok(result.Value) : ApiProblems.NotFound(result.Message!);
        }
        catch (GitHubIssuesException exception)
        {
            return ApiProblems.GitHubEligibilityFailed($"Could not load current GitHub specs. Check GitHub access and refresh. {exception.Message}");
        }
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
        var feedback = new SpecSelectionResult(request.SpecIssueNumber, result);
        return result.Outcome switch
        {
            EnqueueOutcome.Queued => TypedResults.Accepted($"/api/spec-runs/{result.SpecRunId!.Value.Value}", new QueueSpecResponse(result.SpecRunId.Value.Value, nameof(EnqueueOutcome.Queued))),
            EnqueueOutcome.AlreadyQueued => TypedResults.Ok(new QueueSpecResponse(result.SpecRunId!.Value.Value, nameof(EnqueueOutcome.AlreadyQueued))),
            EnqueueOutcome.RepositoryNotFound => ApiProblems.NotFound(feedback.Message),
            EnqueueOutcome.Ineligible => ApiProblems.Conflict(feedback.Message),
            EnqueueOutcome.GitHubUnavailable => ApiProblems.GitHubEligibilityFailed(feedback.Message),
            _ => ApiProblems.Conflict(feedback.Message),
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
