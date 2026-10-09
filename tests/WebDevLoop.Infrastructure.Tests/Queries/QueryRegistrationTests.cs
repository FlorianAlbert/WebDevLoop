using Microsoft.Extensions.DependencyInjection;
using WebDevLoop.Core.Ports;
using WebDevLoop.Core.Queries;
using WebDevLoop.Infrastructure.Persistence;
using WebDevLoop.Infrastructure.Queries;

namespace WebDevLoop.Infrastructure.Tests.Queries;

public sealed class QueryRegistrationTests
{
    [Fact]
    public void queries_and_the_shared_persistent_agent_log_store_resolve()
    {
        ServiceProvider provider = new ServiceCollection()
            .AddPersistence("Data Source=:memory:")
            .AddQueries()
            .BuildServiceProvider(validateScopes: true);
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<EfRunQueries>(scope.ServiceProvider.GetRequiredService<IRunQueries>());
        Assert.IsType<EfRepositoryQueries>(scope.ServiceProvider.GetRequiredService<IRepositoryQueries>());
        Assert.IsType<PersistentAgentLogStore>(provider.GetRequiredService<IAgentLogSink>());
        Assert.Same(provider.GetRequiredService<IAgentLogSink>(), provider.GetRequiredService<IAgentLogReader>());
        Assert.Same(provider.GetRequiredService<IAgentLogSink>(), provider.GetRequiredService<IAgentLogNotifications>());
    }
}
