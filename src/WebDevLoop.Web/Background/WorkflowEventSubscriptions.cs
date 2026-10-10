using WebDevLoop.Core.Events;
using WebDevLoop.Core.Orchestration.Attention;
using WebDevLoop.Core.Orchestration.Completion.ParentReview;
using WebDevLoop.Core.Orchestration.Completion.ReadyAndMerge;
using WebDevLoop.Core.Orchestration.Completion.Testing;
using WebDevLoop.Core.Orchestration.Frontier;
using WebDevLoop.Core.Orchestration.Integration;
using WebDevLoop.Core.Orchestration.Preparation;
using WebDevLoop.Core.Orchestration.ReviewLoop;
using WebDevLoop.Core.Orchestration.SpecQueue;

namespace WebDevLoop.Web.Background;

/// <summary>
/// Subscribes every workflow event handler to the in-process bus when the host starts, i.e. before startup recovery replays
/// the outbox. Each delivery runs the handler in a DI scope of its own and is deduplicated per handler by outbox message id
/// (delivery is at-least-once).
/// </summary>
public sealed class WorkflowEventSubscriptions(IRunEventBus bus, IServiceScopeFactory scopes) : IHostedService
{
    private readonly object _gate = new();
    private readonly List<IDisposable> _subscriptions = [];

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Subscribe<SpecQueueEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<PreparationEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<FrontierEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<ReviewLoopEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<IntegrationEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<ParentReviewEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<TestingEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<CompletionEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        Subscribe<AttentionTriageEventHandler>((handler, envelope, token) => handler.HandleAsync(envelope, token));
        return Task.CompletedTask;
    }

    /// <summary>Idempotent and safe when the host stops twice concurrently.</summary>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            subscriptions = [.. _subscriptions];
            _subscriptions.Clear();
        }

        foreach (IDisposable subscription in subscriptions)
        {
            subscription.Dispose();
        }

        return Task.CompletedTask;
    }

    private void Subscribe<THandler>(Func<THandler, EventEnvelope, CancellationToken, Task> handle)
        where THandler : notnull
    {
        var deduplicated = new DeduplicatingEventHandler(async (envelope, cancellationToken) =>
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await handle(scope.ServiceProvider.GetRequiredService<THandler>(), envelope, cancellationToken);
        });
        IDisposable subscription = bus.Subscribe(deduplicated.HandleAsync);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }
    }
}
