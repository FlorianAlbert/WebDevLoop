using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

internal static class StepEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("steps").WithTags("Steps");

        group.MapGet("{id}", async (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ApiIds.Step(id) is { } stepId && await queries.GetStepAsync(stepId, cancellationToken) is { } step
                ? (IResult)TypedResults.Ok(step)
                : NotFound(id))
            .WithName("GetStep")
            .Produces<StepRunView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("{id}/logs", async (
            string id,
            [FromServices] IRunQueries queries,
            [FromServices] IAgentLogReader logs,
            CancellationToken cancellationToken,
            [FromQuery] int after = 0) =>
        {
            if (ApiIds.Step(id) is not { } stepId || await queries.GetStepAsync(stepId, cancellationToken) is null)
            {
                return NotFound(id);
            }

            int cursor = Math.Max(after, 0);
            IReadOnlyList<AgentLogView> entries = await logs.ReadAsync(stepId, cursor, cancellationToken);
            return TypedResults.Ok(new AgentLogsResponse(id, entries, entries.Count > 0 ? entries[^1].Sequence : cursor));
        })
            .WithName("GetStepLogs")
            .WithSummary("Live agent output of a step. Pass the returned lastSequence as 'after' to fetch only new entries.")
            .Produces<AgentLogsResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult NotFound(string id) => ApiProblems.NotFound($"Step '{id}' does not exist.");
}
