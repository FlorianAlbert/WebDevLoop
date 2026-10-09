using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Recovery.Startup;
using WebDevLoop.Core.Ports;
using WebDevLoop.Infrastructure.Events;
using WebDevLoop.Infrastructure.Persistence;

namespace WebDevLoop.Infrastructure.Tests.Events;

public sealed class EventingRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 10, 9, 12, 0, 0, TimeSpan.Zero)));
        services.AddPersistence("Data Source=:memory:");
        services.AddEventing();
        return services.BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public void Outbox_dispatcher_and_signal_resolve_per_scope_on_top_of_the_persistence_outbox()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<EfOutbox>(scope.ServiceProvider.GetRequiredService<IOutbox>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<OutboxDispatcher>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<FrontierReconciliationSignal>());
    }

    [Fact]
    public void Recovery_stages_of_the_eventing_feature_resolve_to_the_scoped_dispatcher_and_signal()
    {
        using ServiceProvider provider = BuildProvider();
        using IServiceScope scope = provider.CreateScope();

        Assert.Same(scope.ServiceProvider.GetRequiredService<OutboxDispatcher>(), scope.ServiceProvider.GetRequiredService<IOutboxReplay>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<FrontierReconciliationSignal>(), scope.ServiceProvider.GetRequiredService<IFrontierReconciliationTrigger>());
    }

    [Fact]
    public void The_event_bus_is_one_shared_instance_so_subscribers_see_events_dispatched_from_any_scope()
    {
        using ServiceProvider provider = BuildProvider();

        IRunEventBus first = provider.GetRequiredService<IRunEventBus>();
        IRunEventBus second = provider.GetRequiredService<IRunEventBus>();

        Assert.IsType<InProcessRunEventBus>(first);
        Assert.Same(first, second);
    }
}
