using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Domain;
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
            .WithSummary("Connects a repository after checking its fixed webdevloop label.")
            .WithDescription("If the label is absent, returns 409 with code label_creation_approval_required. Resubmit with approveLabelCreation: true only after explicit user approval. Omitted or false approval never creates the label. Label failures return 503; retry after checking GitHub access. Existing issues are never labeled by setup.")
            .Produces<RepositoryView>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("{repoId:int}/opt-in", async (int repoId, [FromServices] IRepositoryQueries queries,
            [FromServices] RepositoryOptIn optIn, CancellationToken cancellationToken) =>
            await EnsureOptInAsync(repoId, false, queries, optIn, cancellationToken))
            .WithName("CheckRepositoryOptIn")
            .WithSummary("Checks the repository label without creating it. A missing label pauses new work, not the connection.")
            .Produces<bool>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        group.MapPost("{repoId:int}/opt-in", async (int repoId, [FromBody] ApproveRepositoryLabelCommand command,
            [FromServices] IRepositoryQueries queries, [FromServices] RepositoryOptIn optIn, CancellationToken cancellationToken) =>
            await EnsureOptInAsync(repoId, command.ApproveLabelCreation, queries, optIn, cancellationToken))
            .WithName("ApproveRepositoryOptInLabel")
            .WithSummary("Creates a missing webdevloop label only with approveLabelCreation: true; preserves repository settings and existing issue labels.")
            .Produces<bool>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

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

    private static async Task<IResult> EnsureOptInAsync(int repositoryId, bool approval, IRepositoryQueries queries,
        RepositoryOptIn optIn, CancellationToken cancellationToken)
    {
        RepositoryView? repository = await queries.GetAsync(repositoryId, cancellationToken);
        if (repository is null)
        {
            return ApiProblems.NotFound($"Repository {repositoryId} does not exist.");
        }

        CommandResult<bool> result = await optIn.EnsureAsync(new GitHubRepoRef(repository.Owner, repository.Name), approval, cancellationToken);
        return result.IsSuccess ? TypedResults.Ok(true) : ApiProblems.ForFailure(result);
    }
}
