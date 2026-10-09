using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Domain;
using WebDevLoop.Core.Orchestration.Control;
using WebDevLoop.Core.Queries;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

/// <summary>Retry/Skip/Abort commands. Rejected with the prerequisite status in diagnostic-only mode; 200 returns the updated run.</summary>
internal static class RunControlEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Run control").AddEndpointFilter<OperationalOnlyFilter>();

        group.MapPost("spec-runs/{id}/retry", (string id, [FromServices] IRunControl control, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            SpecRunCommandAsync(id, ControlAction.Retry, queries, runId => control.RetrySpecAsync(runId, cancellationToken), cancellationToken))
            .WithName("RetrySpecRun")
            .WithSummary("Resumes a spec run that needs attention at its failed phase (claiming a free active-spec slot when needed).")
            .WithControlResponses<SpecRunView>();

        group.MapPost("spec-runs/{id}/abort", (string id, [FromServices] IRunControl control, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            SpecRunCommandAsync(id, ControlAction.Abort, queries, runId => control.AbortSpecAsync(runId, cancellationToken), cancellationToken))
            .WithName("AbortSpecRun")
            .WithSummary("Aborts a spec run: stops its agent sessions and tester app, cancels its steps, aborts its open tickets, and releases its slot.")
            .WithControlResponses<SpecRunView>();

        group.MapPost("ticket-runs/{id}/retry", (string id, [FromServices] IRunControl control, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            TicketRunCommandAsync(id, ControlAction.Retry, queries, ticketId => control.RetryTicketAsync(ticketId, cancellationToken), cancellationToken))
            .WithName("RetryTicketRun")
            .WithSummary("Resumes a ticket run that needs attention: implement again, review again, or resume its integration.")
            .WithControlResponses<TicketRunView>();

        group.MapPost("ticket-runs/{id}/skip", (
                string id,
                [FromBody] SkipTicketRequest? request,
                [FromServices] IRunControl control,
                [FromServices] IRunQueries queries,
                CancellationToken cancellationToken) =>
            TicketRunCommandAsync(
                id,
                ControlAction.Skip,
                queries,
                ticketId => control.SkipTicketAsync(ticketId, request?.Dependents ?? SkipDependents.Unblock, cancellationToken),
                cancellationToken))
            .WithName("SkipTicketRun")
            .WithSummary("Skips a ticket run that is not being worked on. Its dependents start without it (Unblock, default) or are skipped too (Skip).")
            .WithControlResponses<TicketRunView>()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapPost("ticket-runs/{id}/abort", (string id, [FromServices] IRunControl control, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            TicketRunCommandAsync(id, ControlAction.Abort, queries, ticketId => control.AbortTicketAsync(ticketId, cancellationToken), cancellationToken))
            .WithName("AbortTicketRun")
            .WithSummary("Aborts a ticket run: cancels its active steps and aborts their agent sessions. Its dependents stay blocked.")
            .WithControlResponses<TicketRunView>();
    }

    private static RouteHandlerBuilder WithControlResponses<TRun>(this RouteHandlerBuilder builder) => builder
        .Produces<RunControlResponse<TRun>>()
        .ProducesProblem(StatusCodes.Status404NotFound)
        .ProducesProblem(StatusCodes.Status409Conflict)
        .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

    private static async Task<IResult> SpecRunCommandAsync(
        string id,
        ControlAction action,
        IRunQueries queries,
        Func<RunId, Task<ControlResult>> command,
        CancellationToken cancellationToken)
    {
        if (ApiIds.SpecRun(id) is not { } runId)
        {
            return ApiProblems.NotFound($"Spec run '{id}' does not exist.");
        }

        ControlResult result = await command(runId);
        if (!result.IsApplied)
        {
            return ApiProblems.ForControl(result);
        }

        return await queries.GetSpecRunAsync(runId, cancellationToken) is { } run
            ? TypedResults.Ok(new RunControlResponse<SpecRunView>(action.ToString(), run, result.Warnings ?? []))
            : ApiProblems.NotFound($"Spec run '{id}' does not exist.");
    }

    private static async Task<IResult> TicketRunCommandAsync(
        string id,
        ControlAction action,
        IRunQueries queries,
        Func<TicketRunId, Task<ControlResult>> command,
        CancellationToken cancellationToken)
    {
        if (ApiIds.TicketRun(id) is not { } ticketId)
        {
            return ApiProblems.NotFound($"Ticket run '{id}' does not exist.");
        }

        ControlResult result = await command(ticketId);
        if (!result.IsApplied)
        {
            return ApiProblems.ForControl(result);
        }

        return await queries.GetTicketRunAsync(ticketId, cancellationToken) is { } ticket
            ? TypedResults.Ok(new RunControlResponse<TicketRunView>(action.ToString(), ticket, result.Warnings ?? []))
            : ApiProblems.NotFound($"Ticket run '{id}' does not exist.");
    }
}
