using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Management;

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
    /// Registers the Core application services behind the endpoints. Their dependencies come from the persistence, eventing,
    /// settings and spec-queue registrations: the repository/settings/spec-run ports, <c>IUnitOfWork</c>, <c>IClock</c>,
    /// <c>IEffectiveSettingsProvider</c> and <c>SpecQueueService</c>.
    /// </summary>
    public static IServiceCollection AddWebDevLoopApplicationServices(this IServiceCollection services)
    {
        services.TryAddScoped<IRepositoryRegistry, RepositoryRegistry>();
        services.TryAddScoped<ISettingsManager, SettingsManager>();
        services.TryAddScoped<ISpecEnqueuer, SpecEnqueuer>();
        return services;
    }
}
