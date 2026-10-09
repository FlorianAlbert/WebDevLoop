using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;

namespace WebDevLoop.Infrastructure.Queries;

public static class QueryServiceCollectionExtensions
{
    /// <summary>Registers the read-model queries (scoped, on <c>AddPersistence</c>) and the shared live agent log store as sink and reader.</summary>
    public static IServiceCollection AddQueries(this IServiceCollection services)
    {
        services.TryAddScoped<IRepositoryQueries, EfRepositoryQueries>();
        services.TryAddScoped<IRunQueries, EfRunQueries>();
        services.TryAddSingleton(new AgentLogStoreOptions());
        services.TryAddSingleton<InMemoryAgentLogStore>();
        services.TryAddSingleton<IAgentLogSink>(provider => provider.GetRequiredService<InMemoryAgentLogStore>());
        services.TryAddSingleton<IAgentLogReader>(provider => provider.GetRequiredService<InMemoryAgentLogStore>());
        return services;
    }
}
