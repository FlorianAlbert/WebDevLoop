using Microsoft.AspNetCore.Mvc;
using WebDevLoop.Infrastructure.Prerequisites;
using WebDevLoop.Web.Api.Contracts;

namespace WebDevLoop.Web.Api;

internal static class HealthEndpoints
{
    public static void Map(IEndpointRouteBuilder api)
    {
        RouteGroupBuilder group = api.MapGroup(string.Empty).WithTags("Health");

        group.MapGet("health", ([FromServices] DiagnosticReadiness readiness) =>
        {
            HealthResponse health = readiness.Current.ToHealth();
            return health.Mode == ReadinessMode.Operational
                ? Results.Ok(health)
                : Results.Json(health, statusCode: StatusCodes.Status503ServiceUnavailable);
        })
            .WithName("GetHealth")
            .WithSummary("Readiness: 200 when operational, 503 in diagnostic-only mode.")
            .Produces<HealthResponse>()
            .Produces<HealthResponse>(StatusCodes.Status503ServiceUnavailable);

        group.MapGet("prerequisites", ([FromServices] DiagnosticReadiness readiness) => TypedResults.Ok(readiness.Current.ToPrerequisites()))
            .WithName("GetPrerequisites")
            .WithSummary("Latest prerequisite checks with remediation; always available, also in diagnostic-only mode.");
    }
}
