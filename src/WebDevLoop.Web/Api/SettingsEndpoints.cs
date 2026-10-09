using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Core.Management;

namespace WebDevLoop.Web.Api;

internal static class SettingsEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Settings");

        group.MapGet("settings/global", async ([FromServices] ISettingsManager settings, CancellationToken cancellationToken) =>
            TypedResults.Ok(await settings.GetGlobalAsync(cancellationToken)))
            .WithName("GetGlobalSettings")
            .Produces<SettingsProfileData>();

        group.MapPut("settings/global", async ([FromBody] SettingsProfileData data, [FromServices] ISettingsManager settings, CancellationToken cancellationToken) =>
            ToResult(await settings.SaveGlobalAsync(data, cancellationToken)))
            .WithName("PutGlobalSettings")
            .WithSummary("Replaces the global settings layer, including role prompt templates. Validated before saving.")
            .Produces<SettingsProfileData>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("repos/{repoId:int}/settings", async (int repoId, [FromServices] ISettingsManager settings, CancellationToken cancellationToken) =>
            ToResult(await settings.GetRepositoryAsync(repoId, cancellationToken)))
            .WithName("GetRepositorySettings")
            .Produces<SettingsProfileData>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPut("repos/{repoId:int}/settings", async (int repoId, [FromBody] SettingsProfileData data, [FromServices] ISettingsManager settings, CancellationToken cancellationToken) =>
            ToResult(await settings.SaveRepositoryAsync(repoId, data, cancellationToken)))
            .WithName("PutRepositorySettings")
            .WithSummary("Replaces the repository's override layer; unset values fall through to global settings and defaults.")
            .Produces<SettingsProfileData>()
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("settings/effective/{repoId:int}", async (int repoId, [FromServices] ISettingsManager settings, CancellationToken cancellationToken) =>
            ToResult(await settings.GetEffectiveAsync(repoId, cancellationToken)))
            .WithName("GetEffectiveSettings")
            .WithSummary("Fully resolved settings (repository override, then global, then defaults).")
            .Produces<EffectiveSettingsView>()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static IResult ToResult<T>(CommandResult<T> result) => result.IsSuccess ? TypedResults.Ok(result.Value) : ApiProblems.ForFailure(result);
}
