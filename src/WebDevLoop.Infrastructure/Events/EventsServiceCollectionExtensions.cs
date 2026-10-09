using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WebDevLoop.Core.Events;

namespace WebDevLoop.Infrastructure.Events;

public static class EventsServiceCollectionExtensions
{
    /// <summary>
    /// Registers the durable outbox (scoped, on <c>AddPersistence</c>'s outbox storage), the shared in-process event bus,
    /// the dispatcher and the reconciliation signal. Requires <c>IClock</c> and <c>AddPersistence</c>.
    /// </summary>
    public static IServiceCollection AddEventing(this IServiceCollection services)
    {
        services.TryAddSingleton<IRunEventBus, InProcessRunEventBus>();
        services.TryAddSingleton(new OutboxDispatcherOptions());
        services.TryAddScoped<IOutbox, EfOutbox>();
        services.TryAddScoped<OutboxDispatcher>();
        services.TryAddScoped<FrontierReconciliationSignal>();

        return services;
    }
}
