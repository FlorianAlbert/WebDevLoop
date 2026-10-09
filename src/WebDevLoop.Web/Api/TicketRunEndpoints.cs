using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Api;

internal static class TicketRunEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("ticket-runs").WithTags("Ticket runs");

        group.MapGet("{id}", async (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ApiIds.TicketRun(id) is { } ticketId && await queries.GetTicketRunAsync(ticketId, cancellationToken) is { } ticket
                ? (IResult)TypedResults.Ok(ticket)
                : NotFound(id))
            .WithName("GetTicketRun")
            .Produces<TicketRunView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("{id}/steps", async (string id, [FromServices] IRunQueries queries, CancellationToken cancellationToken) =>
            ApiIds.TicketRun(id) is { } ticketId && await queries.GetTicketRunAsync(ticketId, cancellationToken) is not null
                ? (IResult)TypedResults.Ok(await queries.ListStepsAsync(ticketId, cancellationToken))
                : NotFound(id))
            .WithName("ListTicketRunSteps")
            .Produces<IReadOnlyList<StepRunView>>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static IResult NotFound(string id) => ApiProblems.NotFound($"Ticket run '{id}' does not exist.");
}
