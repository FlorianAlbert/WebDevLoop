using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Management;
using WebDevLoop.Core.Orchestration.Control;

namespace WebDevLoop.Web.DependencyInjection;

public static class WebApiServiceCollectionExtensions
{
    /// <summary>Registers the API's JSON conventions (string enums), OpenAPI document generation and the UI selection context. Pair with <see cref="AddWebDevLoopApplicationServices"/>.</summary>
    public static IServiceCollection AddWebDevLoopApi(this IServiceCollection services)
    {
        services.Configure<JsonOptions>(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        services.AddOpenApi();
        services.TryAddSingleton<ICurrentRepositorySelection, CurrentRepositorySelection>();
        return services;
    }

    /// <summary>
    /// Registers the Core application services behind the endpoints and UI. Their dependencies come from the persistence,
    /// eventing, settings, spec-queue and orchestration registrations: the repository/settings/run ports, <c>IUnitOfWork</c>,
    /// <c>IOutbox</c>, <c>IClock</c>, <c>IEffectiveSettingsProvider</c>, <c>SettingsResolver</c>, <c>SpecQueueService</c>, and
    /// for the run controls <c>SpecQueueScheduler</c>, the singleton <c>RepositoryIntegrationGate</c>, <c>IAgentRunner</c> and
    /// <c>ITestTargetRunner</c>.
    /// </summary>
    public static IServiceCollection AddWebDevLoopApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<IWorkspaceRootProvider, GlobalWorkspaceRootProvider>();
        services.TryAddScoped<IRepositoryRegistry, RepositoryRegistry>();
        services.TryAddScoped<ISettingsManager, SettingsManager>();
        services.TryAddScoped<ISpecEnqueuer, SpecEnqueuer>();
        services.TryAddSingleton(RunControlOptions.Default);
        services.TryAddScoped<RunControlJournal>();
        services.TryAddScoped<ActiveWorkStopper>();
        services.TryAddScoped<SpecRunControl>();
        services.TryAddScoped<TicketRunControl>();
        services.TryAddScoped<IRunControl, RunControlService>();
        return services;
    }
}
