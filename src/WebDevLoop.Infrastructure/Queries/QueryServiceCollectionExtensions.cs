using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Infrastructure.Queries;

public static class QueryServiceCollectionExtensions
{
    /// <summary>Registers the read-model queries (scoped, on <c>AddPersistence</c>) and the shared SQLite-backed agent log store (batched appends, retention cap) as sink and reader.</summary>
    public static IServiceCollection AddQueries(this IServiceCollection services)
    {
        services.TryAddScoped<IRepositoryQueries, EfRepositoryQueries>();
        services.TryAddScoped<IRunQueries, EfRunQueries>();
        services.TryAddSingleton(new AgentLogStoreOptions());
        services.TryAddSingleton<PersistentAgentLogStore>();
        services.TryAddSingleton<IAgentLogSink>(provider => provider.GetRequiredService<PersistentAgentLogStore>());
        services.TryAddSingleton<IAgentLogReader>(provider => provider.GetRequiredService<PersistentAgentLogStore>());
        services.TryAddSingleton<IAgentLogNotifications>(provider => provider.GetRequiredService<PersistentAgentLogStore>());
        return services;
    }
}
