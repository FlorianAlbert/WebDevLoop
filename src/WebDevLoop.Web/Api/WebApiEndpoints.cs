namespace WebDevLoop.Web.Api;

public static class WebApiEndpoints
{
    private const string ApiPrefix = "/api";

    /// <summary>Maps the REST API under <c>/api</c> and the OpenAPI document. Needs <c>AddWebDevLoopApi</c> and the Core/Infrastructure services it uses.</summary>
    public static IEndpointRouteBuilder MapWebDevLoopApi(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder api = endpoints.MapGroup(ApiPrefix);
        HealthEndpoints.Map(api);
        RepositoryEndpoints.Map(api);
        SettingsEndpoints.Map(api);
        SpecRunEndpoints.Map(api);
        TicketRunEndpoints.Map(api);
        StepEndpoints.Map(api);
        RunControlEndpoints.Map(api);
        EventStreamEndpoints.Map(api);

        endpoints.MapOpenApi();
        return endpoints;
    }
}
