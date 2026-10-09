using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Web.Api;

internal static class RepositoryEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup("repos").WithTags("Repositories");

        group.MapGet(string.Empty, async ([FromServices] IRepositoryQueries queries, CancellationToken cancellationToken) =>
            TypedResults.Ok(await queries.ListAsync(cancellationToken)))
            .WithName("ListRepositories")
            .Produces<IReadOnlyList<RepositoryView>>();

        group.MapPost(string.Empty, async ([FromBody] RegisterRepositoryCommand command, [FromServices] IRepositoryRegistry registry, CancellationToken cancellationToken) =>
        {
            CommandResult<RepositoryView> result = await registry.RegisterAsync(command, cancellationToken);
            return result.IsSuccess
                ? TypedResults.Created($"/api/repos/{result.Value!.Id}", result.Value)
                : ApiProblems.ForFailure(result);
        })
            .WithName("RegisterRepository")
            .Produces<RepositoryView>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("{repoId:int}", async (int repoId, [FromServices] IRepositoryQueries queries, CancellationToken cancellationToken) =>
            await queries.GetAsync(repoId, cancellationToken) is { } repository
                ? (IResult)TypedResults.Ok(repository)
                : ApiProblems.NotFound($"Repository {repoId} does not exist."))
            .WithName("GetRepository")
            .Produces<RepositoryView>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPatch("{repoId:int}", async (int repoId, [FromBody] UpdateRepositoryCommand command, [FromServices] IRepositoryRegistry registry, CancellationToken cancellationToken) =>
        {
            CommandResult<RepositoryView> result = await registry.UpdateAsync(repoId, command, cancellationToken);
            return result.IsSuccess ? TypedResults.Ok(result.Value) : ApiProblems.ForFailure(result);
        })
            .WithName("UpdateRepository")
            .Produces<RepositoryView>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapDelete("{repoId:int}", async (int repoId, [FromServices] IRepositoryRegistry registry, CancellationToken cancellationToken) =>
        {
            CommandResult<int> result = await registry.RemoveAsync(repoId, cancellationToken);
            return result.IsSuccess ? TypedResults.NoContent() : ApiProblems.ForFailure(result);
        })
            .WithName("RemoveRepository")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapPost("{repoId:int}/select", async (
            int repoId,
            [FromServices] IRepositoryQueries queries,
            [FromServices] ICurrentRepositorySelection selection,
            CancellationToken cancellationToken) =>
        {
            if (await queries.GetAsync(repoId, cancellationToken) is not { } repository)
            {
                return ApiProblems.NotFound($"Repository {repoId} does not exist.");
            }

            selection.Select(repoId);
            return (IResult)TypedResults.Ok(repository);
        })
            .WithName("SelectRepository")
            .WithSummary("Sets the repository the UI is viewing. View context only; never starts, stops or filters scheduling.")
            .Produces<RepositoryView>()
            .ProducesProblem(StatusCodes.Status404NotFound);
    }
}
